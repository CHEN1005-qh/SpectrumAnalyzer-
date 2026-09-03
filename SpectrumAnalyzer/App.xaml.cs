using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace SpectrumAnalyzer
{
    /// <summary>
    /// App.xaml 的交互逻辑
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // 注册全局异常处理，便于在运行时捕获并记录未处理异常，帮助定位测试中出现的问题
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

            base.OnStartup(e);
        }

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            HandleException(e.Exception, "UI 线程未处理的异常");
            e.Handled = true;
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var ex = e.ExceptionObject as Exception;
            HandleException(ex, "AppDomain 未处理的异常");
        }

        private void TaskScheduler_UnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            HandleException(e.Exception, "任务调度未观察到的异常");
            e.SetObserved();
        }

        private void HandleException(Exception ex, string title)
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string logPath = System.IO.Path.Combine(baseDir, "error.log");
                string content = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {title}\r\n" + (ex?.ToString() ?? "(null)") + "\r\n\r\n";
                System.IO.File.AppendAllText(logPath, content);

                // 同步显示错误给用户，便于测试时立刻反馈
                System.Windows.MessageBox.Show($"发生未处理异常，已记录到:\r\n{logPath}\r\n\r\n错误摘要：{ex?.Message}", "运行时错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch
            {
                // 写日志或显示失败时忽略，避免二次抛出
            }
        }
    }
}
