# notify.ps1 — show a Windows 10/11 toast notification.
#
# Usage:
#   powershell.exe -NoProfile -ExecutionPolicy Bypass ^
#     -File notify.ps1 "<title>" "<message>"
#
# Uses the Windows.UI.Notifications WinRT API via the
# PowerShell [Type] accelerator. PowerShell 5.1+ (bundled with
# Win10+) supports this pattern; on PowerShell 7+ the same
# syntax works. The AUMID ("ApertureNeo") is the App User
# Model ID; Windows uses it to group toasts under a stable
# app identity rather than the calling process.
#
# Failures (missing WinRT, unsupported PS edition, no toast
# runtime) are best-effort — the script prints an error to
# stderr and exits non-zero, but the caller (ApertureNeo.exe)
# treats the toast as fire-and-forget so the OCR result is
# still copied to the clipboard.

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)] [string] $Title,
    [Parameter(Mandatory = $true, Position = 1)] [string] $Message
)

$ErrorActionPreference = 'Stop'

try {
    # Load Windows.UI.Notifications + Windows.UI WinRT types. The
    # ContentType=WindowsRuntime hint tells the [Type] accelerator
    # to use WinRT activation, not .NET reflection.
    [Windows.UI.Notifications.ToastNotificationManager, Windows.UI, ContentType = WindowsRuntime] | Out-Null
    [Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null

    # ToastText02 = "title + message body" (two text fields).
    $template = [Windows.UI.Notifications.ToastNotificationManager]::GetTemplateContent(
        [Windows.UI.Notifications.ToastTemplateType]::ToastText02
    )

    $textNodes = $template.GetElementsByTagName('text')
    $textNodes[0].AppendChild($template.CreateTextNode($Title)) | Out-Null
    $textNodes[1].AppendChild($template.CreateTextNode($Message)) | Out-Null

    $toast = [Windows.UI.Notifications.ToastNotification]::new($template)

    # AUMID: any stable string. Windows displays toasts under this
    # name in Action Center. The exe's ProductName would be ideal
    # but we don't want to parse the .exe for it; "ApertureNeo"
    # matches MyAppName in installer.iss and is stable.
    $notifier = [Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('ApertureNeo')
    $notifier.Show($toast)

    exit 0
}
catch {
    [Console]::Error.WriteLine("notify.ps1: $($_.Exception.Message)")
    exit 1
}
