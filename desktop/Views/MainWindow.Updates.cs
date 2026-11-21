using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Cadenza.Core.Exceptions;
using Cadenza.Core.Models;
using Microsoft.UI.Xaml;

namespace Cadenza.Desktop.Views;

public sealed partial class MainWindow
{
    private AppUpdate? _availableUpdate;
    private CancellationTokenSource? _updateOperation;

    private static string CurrentAppVersion => typeof(MainWindow).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0-dev";

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_updateOperation is not null || _closing)
            return;

        _availableUpdate = null;
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _updateOperation = operation;
        SetUpdateBusy(true);
        UpdateStatus.Text = "Checking GitHub releases…";
        try
        {
            _availableUpdate = await _services.Updates.CheckAsync(CurrentAppVersion, PreviewUpdates.IsChecked == true, operation.Token);
            if (!_closing)
                UpdateStatus.Text = _availableUpdate is null ? "You're up to date for the selected release channel."
                    : $"{_availableUpdate.Version} is available ({_availableUpdate.Size / (1024.0 * 1024):F1} MB).";
        }
        catch (OperationCanceledException)
        {
            if (!_closing)
                UpdateStatus.Text = "Update check canceled or timed out. You can retry.";
        }
        catch (Exception error) when (error is HttpRequestException or AppUpdateException or JsonException or InvalidOperationException or KeyNotFoundException)
        {
            if (!_closing)
                UpdateStatus.Text = "Couldn't check for updates. Check your connection or try again later.";
        }
        finally
        {
            _updateOperation = null;
            if (!_closing)
                SetUpdateBusy(false);
        }
    }

    private async void InstallUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_availableUpdate is not { } update || _updateOperation is not null || _closing)
            return;

        using var operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _updateOperation = operation;
        SetUpdateBusy(true);
        UpdateStatus.Text = "Downloading and verifying the installer…";
        try
        {
            var path = await _services.Updates.DownloadAsync(update, _services.DataPath("updates"), operation.Token);
            operation.Token.ThrowIfCancellationRequested();
            if (_closing)
                return;

            // Keep the normal setup wizard and Windows trust prompts. AppId lets Inno Setup
            // upgrade the existing installation; its post-install action offers to reopen Cadenza.
            using var installer = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })
                ?? throw new AppUpdateException("Windows couldn't start the installer. Try again.");
            Close();
        }
        catch (OperationCanceledException)
        {
            if (!_closing)
                UpdateStatus.Text = "Update download canceled or timed out. You can retry.";
        }
        catch (Exception error) when (error is HttpRequestException or AppUpdateException or IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
        {
            if (!_closing)
                UpdateStatus.Text = error is AppUpdateException ? error.Message : "Couldn't download or start the update. Retry when your connection and Windows permissions allow it.";
        }
        finally
        {
            _updateOperation = null;
            if (!_closing)
                SetUpdateBusy(false);
        }
    }

    private void CancelUpdate_Click(object sender, RoutedEventArgs e) => _updateOperation?.Cancel();

    private void SetUpdateBusy(bool busy)
    {
        CheckUpdateButton.IsEnabled = !busy;
        PreviewUpdates.IsEnabled = !busy;
        InstallUpdateButton.IsEnabled = !busy;
        InstallUpdateButton.Visibility = ToVisibility(_availableUpdate is not null);
        CancelUpdateButton.Visibility = ToVisibility(busy);
        UpdateProgress.IsIndeterminate = busy;
        UpdateProgress.Visibility = ToVisibility(busy);
    }
}
