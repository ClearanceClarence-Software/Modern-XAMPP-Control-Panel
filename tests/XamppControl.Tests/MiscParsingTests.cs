using XamppControl.Core;
using XamppControl.Modules;
using XamppControl.Updates;

namespace XamppControl.Tests;

public sealed class MiscParsingTests
{
    [Fact]
    public void ConfigTestProblems_DropsSyntaxOkAndKeepsTheComplaint()
    {
        const string error = "AH00526: Syntax error on line 12 of httpd-vhosts.conf:\nSyntax OK\n";
        var result = new Shell.RunResult(1, "", error, false);

        List<string> problems = ApacheDriver.ConfigTestProblems(result);

        Assert.Single(problems);
        Assert.StartsWith("AH00526", problems[0], StringComparison.Ordinal);
    }

    [Fact]
    public void ConfigTestProblems_NamesTheExitCodeWhenThereIsNoOutput()
    {
        var result = new Shell.RunResult(3, "", "", false);
        Assert.Equal(["httpd -t failed with exit code 3."], ApacheDriver.ConfigTestProblems(result));
    }

    [Fact]
    public void PlainNotes_StripsMarkdownAndTheGeneratedCommitList()
    {
        const string markdown = """
            ### Added
            - **Sites window.** Gives each project its own [address](https://example.test) with `httpd-vhosts.conf`.
              - nested
            ## What's Changed
            * Bump by @someone in https://example.test/pull/1
            """;

        string notes = AppUpdateCheck.PlainNotes(markdown);

        const string expected =
            "Added\n• Sites window. Gives each project its own address with httpd-vhosts.conf.\n  • nested";
        Assert.Equal(expected, notes);
    }

    [Fact]
    public void CrashReport_DescribeIncludesTheInnerException()
    {
        var exception = new InvalidOperationException("outer", new IOException("inner"));

        string text = CrashReport.Describe(exception, "test");

        Assert.Contains("crashed in the test", text, StringComparison.Ordinal);
        Assert.Contains("InvalidOperationException: outer", text, StringComparison.Ordinal);
        Assert.Contains("Caused by:", text, StringComparison.Ordinal);
        Assert.Contains("IOException: inner", text, StringComparison.Ordinal);
    }
}
