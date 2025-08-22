using Microsoft.Toolkit.Uwp.Notifications;
using System.Diagnostics;
using System.Windows;

public partial class App : Application
{
    public App()
    {
        // ① 토스트 활성 핸들러
        ToastNotificationManagerCompat.OnActivated += e =>
        {
            var args = ToastArguments.Parse(e.Argument);
            if (args.TryGetValue("store", out var storeUrl) ||
                args.TryGetValue("url", out storeUrl))
            {
                Process.Start(new ProcessStartInfo { FileName = storeUrl, UseShellExecute = true });
            }
        };

        // ② Notifier 선등록: 시작 메뉴 바로가기/AUMID 보장
        try { ToastNotificationManagerCompat.CreateToastNotifier(); } catch { }
    }
}