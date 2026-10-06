using System.Threading;
using System.Windows;

namespace XinHaiHai;

public partial class App : Application
{
    static Mutex _mutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, "XinHaiHai_心海海_SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("心海海已经在陪你啦,不要开两个哦~", "心海海");
            Shutdown();
            return;
        }

        base.OnStartup(e);
        Store.Init();
        Store.Log("========== 心海海 启动 ==========");

        DispatcherUnhandledException += (s, ex) =>
        {
            Store.Log("未处理异常: " + ex.Exception);
            ex.Handled = true;
        };

        new MainWindow().Show();
    }
}
