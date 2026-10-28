using Avalonia.Controls;
using Avalonia.Interactivity;
using XamppControl.Core;

namespace XamppControl.Views;

/// <summary>
/// Edits the service names, ports and MySQL root password the panel uses; closes with true on Save.
/// </summary>
public partial class ServicePortsWindow : Window
{
    private readonly AppSettings _settings;

    /// <summary>Opens the window on default settings, for the XAML previewer.</summary>
    public ServicePortsWindow() : this(new AppSettings())
    {
    }

    /// <summary>Fills the fields from the settings that Save writes back to.</summary>
    /// <param name="settings">
    /// Its services and root password are replaced only on Save, and only when every port is valid.
    /// </param>
    public ServicePortsWindow(AppSettings settings)
    {
        InitializeComponent();
        AppIcon.Apply(this);
        _settings = settings;
        FillControls(settings.Services, settings.MySqlRootPassword);
    }

    /// <summary>Shows the service settings and the password in the text boxes.</summary>
    /// <param name="services">The names and ports to show; Defaults passes new <see cref="ServiceSettings"/>.</param>
    /// <param name="mySqlPassword">The MySQL root password to show; null leaves the box empty.</param>
    private void FillControls(ServiceSettings services, string? mySqlPassword)
    {
        ApacheService.Text = services.ApacheServiceName;
        ApachePort.Text = services.ApachePort.ToString();
        ApacheSslPort.Text = services.ApacheSslPort.ToString();
        MySqlService.Text = services.MySqlServiceName;
        MySqlPort.Text = services.MySqlPort.ToString();
        MySqlPassword.Text = mySqlPassword;
        FileZillaPort.Text = services.FileZillaPort.ToString();
        FileZillaAdminPort.Text = services.FileZillaAdminPort.ToString();
        TomcatPort.Text = services.TomcatPort.ToString();
        TomcatShutdownPort.Text = services.TomcatShutdownPort.ToString();
        TomcatAjpPort.Text = services.TomcatAjpPort.ToString();
        MercuryPorts.Text = services.MercuryPorts;
    }

    // The password is not a default, so resetting keeps what is typed in its box.
    private void Defaults_Click(object? sender, RoutedEventArgs e) =>
        FillControls(new ServiceSettings(), MySqlPassword.Text);

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        var errors = new List<string>();

        // Collects an error instead of returning early, so every bad port is reported at once.
        int ParsePort(TextBox box, string fieldName)
        {
            if (int.TryParse(box.Text?.Trim(), out int port) && port is > 0 and < 65536)
            {
                return port;
            }

            errors.Add($"{fieldName} must be a number between 1 and 65535.");
            return 0;
        }

        static string NameOrDefault(TextBox box, string fallback) =>
            string.IsNullOrWhiteSpace(box.Text) ? fallback : box.Text.Trim();

        var services = new ServiceSettings
        {
            ApacheServiceName = NameOrDefault(ApacheService, "Apache2.4"),
            ApachePort = ParsePort(ApachePort, "Apache main port"),
            ApacheSslPort = ParsePort(ApacheSslPort, "Apache SSL port"),
            MySqlServiceName = NameOrDefault(MySqlService, "mysql"),
            MySqlPort = ParsePort(MySqlPort, "MySQL port"),
            FileZillaPort = ParsePort(FileZillaPort, "FileZilla main port"),
            FileZillaAdminPort = ParsePort(FileZillaAdminPort, "FileZilla admin port"),
            TomcatPort = ParsePort(TomcatPort, "Tomcat main port"),
            TomcatShutdownPort = ParsePort(TomcatShutdownPort, "Tomcat shutdown port"),
            TomcatAjpPort = ParsePort(TomcatAjpPort, "Tomcat AJP port"),
            MercuryPorts = MercuryPorts.Text?.Trim() ?? "",
        };

        if (errors.Count > 0)
        {
            ErrorText.Text = string.Join(Environment.NewLine, errors);
            ErrorText.IsVisible = true;
            return;
        }

        string? password = MySqlPassword.Text;
        _settings.Services = services;
        _settings.MySqlRootPassword = string.IsNullOrEmpty(password) ? null : password;
        Close(true);
    }

    private void Abort_Click(object? sender, RoutedEventArgs e) => Close(false);
}
