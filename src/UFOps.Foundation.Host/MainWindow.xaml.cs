using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using UFOps.Foundation.Storage;

namespace UFOps.Foundation.Host;

public sealed partial class MainWindow : Window
{
    private readonly FoundationDatabase _database;
    private bool _initialized;

    public MainWindow()
    {
        InitializeComponent();
        var dataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "UFOps",
            "Foundation");
        _database = new FoundationDatabase(Path.Combine(dataRoot, "ufops-foundation.db"));
        DatabasePathText.Text = _database.DatabasePath;
    }

    private async void OnRootLoaded(object sender, RoutedEventArgs e)
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        await RefreshHealthAsync();
    }

    private async void OnRefreshClicked(object sender, RoutedEventArgs e)
    {
        await RefreshHealthAsync();
    }

    private async Task RefreshHealthAsync()
    {
        RefreshButton.IsEnabled = false;
        HealthInfo.IsOpen = true;
        HealthInfo.Severity = InfoBarSeverity.Informational;
        HealthInfo.Title = "Checking foundation health...";
        try
        {
            await _database.InitializeAsync();
            var schema = await _database.GetSchemaVersionAsync();
            var quickCheck = await _database.QuickCheckAsync();
            SchemaVersionText.Text = schema.ToString(System.Globalization.CultureInfo.InvariantCulture);
            QuickCheckText.Text = quickCheck;

            var healthy = schema == 1 && string.Equals(quickCheck, "ok", StringComparison.OrdinalIgnoreCase);
            HealthInfo.Severity = healthy ? InfoBarSeverity.Success : InfoBarSeverity.Error;
            HealthInfo.Title = healthy ? "Foundation health: PASS" : "Foundation health: FAIL";
            HealthInfo.Message = healthy
                ? "Real SQLite initialization, schema verification, and integrity quick_check succeeded."
                : "Foundation storage did not satisfy the expected schema/integrity contract.";
        }
        catch (Exception exception)
        {
            SchemaVersionText.Text = "Unavailable";
            QuickCheckText.Text = "Failed";
            HealthInfo.Severity = InfoBarSeverity.Error;
            HealthInfo.Title = "Foundation health: FAIL";
            HealthInfo.Message = exception.Message;
        }
        finally
        {
            RefreshButton.IsEnabled = true;
        }
    }
}
