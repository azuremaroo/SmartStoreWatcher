using Microsoft.Toolkit.Uwp.Notifications;
using System.Diagnostics;
using System.Windows;

public partial class App : Application
{
    public App()
    {
        // �� �佺Ʈ Ȱ�� �ڵ鷯
        ToastNotificationManagerCompat.OnActivated += e =>
        {
            var args = ToastArguments.Parse(e.Argument);
            if (args.TryGetValue("store", out var storeUrl) ||
                args.TryGetValue("url", out storeUrl))
            {
                Process.Start(new ProcessStartInfo { FileName = storeUrl, UseShellExecute = true });
            }
        };

        // �� Notifier �����: ���� �޴� �ٷΰ���/AUMID ����
        try { ToastNotificationManagerCompat.CreateToastNotifier(); } catch { }
    }
}