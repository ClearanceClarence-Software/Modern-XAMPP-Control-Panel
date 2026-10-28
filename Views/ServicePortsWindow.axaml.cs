using Avalonia.Controls;
using Avalonia.Interactivity;
using XamppControl.Core;

namespace XamppControl.Views;

public partial class ServicePortsWindow : Window
{
    private readonly AppSettings _settings;

    public ServicePortsWindow() : this(new AppSettings()) { }

    public ServicePortsWindow(AppSettings settings)
    {
        InitializeComponent();
        AppIcon.Apply(this);
        _settings = settings;
        Fill(settings.Services, settings.MySqlRootPassword);
    }

    private void Fill(ServiceSettings s, string? password)
    {
        ApacheService.Text = s.ApacheServiceName;
        ApachePort.Text = s.ApachePort.ToString();
        ApacheSslPort.Text = s.ApacheSslPort.ToString();
        MySqlService.Text = s.MySqlServiceName;
        MySqlPort.Text = s.MySqlPort.ToString();
        MySqlPassword.Text = password;
        FileZillaPort.Text = s.FileZillaPort.ToString();
        FileZillaAdminPort.Text = s.FileZillaAdminPort.ToString();
        TomcatPort.Text = s.TomcatPort.ToString();
        TomcatShutdownPort.Text = s.TomcatShutdownPort.ToString();
        TomcatAjpPort.Text = s.TomcatAjpPort.ToString();
        MercuryPorts.Text = s.MercuryPorts;
    }

    private void Defaults_Click(object? sender, RoutedEventArgs e) =>
        Fill(new ServiceSettings(), MySqlPassword.Text);

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        var errors = new List<string>();

        int Port(TextBox box, string name)
        {
            if (int.TryParse(box.Text?.Trim(), out var port) && port is > 0 and < 65536)
                return port;
            errors.Add($"{name} must be a number between 1 and 65535.");
            return 0;
        }

        string NameOr(TextBox box, string fallback) =>
            string.IsNullOrWhiteSpace(box.Text) ? fallback : box.Text.Trim();

        var s = new ServiceSettings
        {
            ApacheServiceName = NameOr(ApacheService, "Apache2.4"),
            ApachePort = Port(ApachePort, "Apache main port"),
            ApacheSslPort = Port(ApacheSslPort, "Apache SSL port"),
            MySqlServiceName = NameOr(MySqlService, "mysql"),
            MySqlPort = Port(MySqlPort, "MySQL port"),
            FileZillaPort = Port(FileZillaPort, "FileZilla main port"),
            FileZillaAdminPort = Port(FileZillaAdminPort, "FileZilla admin port"),
            TomcatPort = Port(TomcatPort, "Tomcat main port"),
            TomcatShutdownPort = Port(TomcatShutdownPort, "Tomcat shutdown port"),
            TomcatAjpPort = Port(TomcatAjpPort, "Tomcat AJP port"),
            MercuryPorts = MercuryPorts.Text?.Trim() ?? "",
        };

        if (errors.Count > 0)
        {
            ErrorText.Text = string.Join(Environment.NewLine, errors);
            ErrorText.IsVisible = true;
            return;
        }

        _settings.Services = s;
        _settings.MySqlRootPassword = string.IsNullOrEmpty(MySqlPassword.Text) ? null : MySqlPassword.Text;
        Close(true);
    }

    private void Abort_Click(object? sender, RoutedEventArgs e) => Close(false);
}
