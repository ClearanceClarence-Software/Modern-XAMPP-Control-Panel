using XamppControl.Php;

namespace XamppControl.Tests;

public sealed class PhpIniTests : IDisposable
{
    private readonly string _folder =
        Path.Combine(Path.GetTempPath(), "XamppControlTests", Guid.NewGuid().ToString("N"));

    public PhpIniTests()
    {
        Directory.CreateDirectory(Path.Combine(_folder, "ext"));
        string[] dlls = ["php_gd.dll", "php_curl.dll", "php_opcache.dll", "php_xdebug-3.4.0-8.4-ts-vs17-x86_64.dll"];
        foreach (string dll in dlls)
        {
            File.WriteAllText(Path.Combine(_folder, "ext", dll), "");
        }
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private static PhpExtension Named(IEnumerable<PhpExtension> extensions, string name) =>
        extensions.Single(extension => extension.Name == name);

    private PhpIni Write(string content)
    {
        string path = Path.Combine(_folder, "php.ini");
        File.WriteAllText(path, content.ReplaceLineEndings("\r\n"));
        return PhpIni.Load(path);
    }

    [Fact]
    public void GetValue_LastActiveLineWinsAndCommentsAreIgnored()
    {
        PhpIni ini = Write("""
            ;memory_limit = 64M
            memory_limit = 128M
            memory_limit = 512M   ; raised for Composer
            upload_max_filesize="40M"
            """);

        Assert.Equal("512M", ini.GetValue("memory_limit"));
        Assert.Equal("40M", ini.GetValue("upload_max_filesize"));
        Assert.Null(ini.GetValue("post_max_size"));
    }

    [Fact]
    public void SetValue_ReplacesTheActiveLineOrUncommentsOrAppends()
    {
        PhpIni ini = Write("""
            memory_limit = 128M
            ;date.timezone =
            """);

        ini.SetValue("memory_limit", "1G");
        ini.SetValue("date.timezone", "Europe/Oslo");
        ini.SetValue("max_input_vars", "5000");
        ini.Save();

        string[] lines = File.ReadAllLines(ini.FilePath);
        string[] expected = ["memory_limit=1G", "date.timezone=Europe/Oslo", "max_input_vars=5000"];
        Assert.Equal(expected, lines);
        Assert.True(File.Exists(ini.FilePath + ".bak"));
    }

    [Fact]
    public void ListExtensions_ReadsTheExtFolderAndWhichLinesAreActive()
    {
        PhpIni ini = Write("""
            extension=gd
            ;extension=curl
            zend_extension=opcache
            """);

        List<PhpExtension> extensions = ini.ListExtensions(Path.Combine(_folder, "ext"));

        string[] expectedNames = ["curl", "gd", "opcache", "xdebug-3.4.0-8.4-ts-vs17-x86_64"];
        Assert.Equal(expectedNames, extensions.Select(extension => extension.Name));
        Assert.True(Named(extensions, "gd").IsEnabled);
        Assert.False(Named(extensions, "curl").IsEnabled);
        Assert.True(Named(extensions, "opcache").IsZend);
        Assert.True(Named(extensions, "xdebug-3.4.0-8.4-ts-vs17-x86_64").IsZend);
    }

    [Fact]
    public void SetExtensionEnabled_UncommentsCommentsOrAddsLines()
    {
        PhpIni ini = Write("""
            extension=gd
            ;extension=curl
            """);
        List<PhpExtension> extensions = ini.ListExtensions(Path.Combine(_folder, "ext"));

        ini.SetExtensionEnabled(Named(extensions, "gd"), false);
        ini.SetExtensionEnabled(Named(extensions, "curl"), true);
        ini.SetExtensionEnabled(Named(extensions, "opcache"), true);
        ini.SetExtensionEnabled(Named(extensions, "xdebug-3.4.0-8.4-ts-vs17-x86_64"), true);
        ini.Save();

        string[] lines = File.ReadAllLines(ini.FilePath);
        Assert.Contains(";extension=gd", lines);
        Assert.Contains("extension=curl", lines);
        Assert.Contains("zend_extension=opcache", lines);
        Assert.Contains("zend_extension=php_xdebug-3.4.0-8.4-ts-vs17-x86_64.dll", lines);
    }

    [Fact]
    public void Save_KeepsLatin1BytesAndLineEndings()
    {
        string path = Path.Combine(_folder, "php.ini");
        byte[] latin1 = System.Text.Encoding.Latin1.GetBytes("; Blåbær\r\nmemory_limit = 128M\r\n");
        File.WriteAllBytes(path, latin1);

        PhpIni ini = PhpIni.Load(path);
        ini.SetValue("memory_limit", "256M");
        ini.Save();

        byte[] saved = File.ReadAllBytes(path);
        Assert.Equal(System.Text.Encoding.Latin1.GetBytes("; Blåbær\r\nmemory_limit=256M\r\n"), saved);
    }
}
