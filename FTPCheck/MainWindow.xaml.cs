using FTPCheck.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Windows.UI.Notifications;

namespace FTPCheck
{
    /// <summary>
    /// MainWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class MainWindow : Window
    {
        private string URL;
        private string TARGET_DIR;
        private int PORT;
        private string USER;
        private string PASSWORD;
        private int DELAY;

        private Dictionary<string, string> ITEM_CACHE = new Dictionary<string, string>();
        private Dictionary<string, string> CURRENT = new Dictionary<string, string>();
        private List<string> DIRS = new List<string>();
        private bool FIRST = true;

        private CancellationTokenSource cts = null;

        private FtpWebRequest Ftp = null;

        public MainWindow()
        {
            InitializeComponent();
            // 設定があれば読み出し
            IniFile ini = new IniFile("./ftp_check.ini");
            String url_ini = ini["setting", "url"];
            String port_ini = ini["setting", "port"];
            String user_ini = ini["setting", "user"];
            String target_dir_ini = ini["setting", "target_dir"];
            String delay_ini = ini["setting", "delay"];
            server_url.Text = url_ini;
            port.Text = port_ini;
            user.Text = user_ini;
            base_dir.Text = target_dir_ini;
            delay.Text = delay_ini;
        }

        private async void execute_button_Click(object sender, RoutedEventArgs e)
        {
            execute_button.IsEnabled = false;
            cancel_button.Visibility = System.Windows.Visibility.Visible;
            clear_button.Visibility = System.Windows.Visibility.Visible;
            bool isCanceled = false;
            using (this.cts = new CancellationTokenSource())
            {
                CancellationToken token = this.cts.Token;
                try
                {
                    await Task.Run(() => Loop(token));
                }
                catch (OperationCanceledException)
                {
                    isCanceled = true;
                }
                catch (Exception ex)
                {
                    WriteLog($"エラーが発生しました。 {ex.Message}");
                }
            }
            if (isCanceled)
            {
                WriteLog("キャンセルされました");
                cancel_button.Visibility = System.Windows.Visibility.Hidden;
                execute_button.Visibility = System.Windows.Visibility.Hidden;
                clear_button.Visibility = System.Windows.Visibility.Hidden;
                execute_button.IsEnabled = true;
                cancel_button.IsEnabled = true;

                prepare_button.IsEnabled = true;
                FIRST = true;
                server_url.IsReadOnly = false;
                base_dir.IsReadOnly = false;
                port.IsReadOnly = false;
                user.IsReadOnly = false;
                delay.IsReadOnly = false;
                prepare_button.Visibility = System.Windows.Visibility.Visible;
                execute_button.Visibility = System.Windows.Visibility.Hidden;
            }
            
        }

        public void WriteLog(string text)
        {
            this.Dispatcher.Invoke((Action)(() =>
            {
                log_text.AppendText("[" + System.DateTime.Now.ToString() + "] " + text + "\r\n");
                log_text.ScrollToEnd();
            }));
        }

        private void Loop(CancellationToken token)
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                WriteLog("確認");
                CURRENT = new Dictionary<string, string>();
                Base();
                List<string> currentKeys = new List<string>();
                foreach (string key in CURRENT.Keys)
                {
                    currentKeys.Add(key);
                }
                List<string> cacheKeys = new List<string>();
                foreach (string key in ITEM_CACHE.Keys)
                {
                    cacheKeys.Add(key);
                }
                if (cacheKeys.Count > currentKeys.Count)
                {
                    foreach (string key in currentKeys)
                    {
                        cacheKeys.Remove(key);
                    }
                    WriteLog($"これらのファイルが無くなったようです: {string.Join(",", cacheKeys)}");
                    Notification("削除通知", $"これらのファイルが無くなったようです: {string.Join(",", cacheKeys)}");
                    foreach (string removeKey in cacheKeys)
                    {
                        ITEM_CACHE.Remove(removeKey);
                    }
                }
                token.ThrowIfCancellationRequested();
                Thread.Sleep(DELAY * 1000 * 60);
            }
        }

        private void Base()
        {
            string[] dirs = GetDirs(null);
            foreach (string dir in dirs)
            {
                string[] bunkai = Regex.Split(dir, " {4,}");
                if (bunkai.Length == 3)
                {
                    DIRS.Add(bunkai[2]);
                    SearchDir(DIRS);
                }
            }
        }

        private void SearchDir(List<string> dirs)
        {
            string[] _dirs = GetDirs(string.Join("/", dirs));
            foreach (string dir in _dirs)
            {
                string[] bunkatsu = Regex.Split(dir, " {4,}");
                if (bunkatsu.Length == 3)
                {
                    DIRS.Add(bunkatsu[2]);
                    SearchDir(DIRS);
                }
                else
                {
                    if (bunkatsu.Length < 2)
                    {
                        continue;
                    }
                    string[] fileNameArr = bunkatsu[1].Split(' ');
                    StringBuilder sb = new StringBuilder();
                    for (var l = 0; l < fileNameArr.Length; l++)
                    {
                        if (l > 0)
                        {
                            sb.Append(fileNameArr[l]);
                        }
                    }
                    string fineName = sb.ToString();
                    string key = string.Join("/", DIRS) + "/" + fineName;
                    if (ITEM_CACHE.ContainsKey(key) && !ITEM_CACHE[key].Equals(bunkatsu[0]))
                    {
                        WriteLog($"ファイルが更新されたようです: {key}");
                        Notification("更新通知", $"ファイルが更新されたようです: {key}");
                    }
                    else if (!FIRST && !ITEM_CACHE.ContainsKey(key))
                    {
                        WriteLog($"新しいファイルが追加されました: {key}");
                        Notification("追加通知", $"新しいファイルが追加されました: {key}");
                    }
                    ITEM_CACHE[key] = bunkatsu[0];
                    CURRENT[key] = bunkatsu[0];

                }
            }
            DIRS.RemoveAt(DIRS.Count - 1);
        }

        private string[] GetDirs(String dirName)
        {
            if (dirName == null)
            {
                Ftp = (FtpWebRequest)WebRequest.Create($"ftp://{URL}:{PORT}" + TARGET_DIR);
            }
            else
            {
                Ftp = (FtpWebRequest)WebRequest.Create($"ftp://{URL}:{PORT}" + TARGET_DIR + "/" + dirName);
            }
            Ftp.Timeout = 5000;
            Ftp.ReadWriteTimeout = 15000;
            Ftp.Credentials = new NetworkCredential(USER, PASSWORD);
            Ftp.Method = WebRequestMethods.Ftp.ListDirectoryDetails;
            FtpWebResponse ftpResponse = (FtpWebResponse)Ftp.GetResponse();
            StreamReader sr = new StreamReader(ftpResponse.GetResponseStream());
            string[] dir = Regex.Split(sr.ReadToEnd(), "\r\n");
            ftpResponse.Close();
            return dir;
        }

        private async void prepare_button_Click(object sender, RoutedEventArgs e)
        {
            prepare_button.IsEnabled = false;
            URL = server_url.Text;
            TARGET_DIR = base_dir.Text;
            if (!int.TryParse(port.Text, out PORT))
            {
                PORT = 21;
            }
            USER = user.Text;
            PASSWORD = password.Password;
            CURRENT = new Dictionary<string, string>();
            ITEM_CACHE = new Dictionary<string, string>();
            try
            {
                DELAY = int.Parse(delay.Text);
                WriteLog("初回準備");
                await Task.Run(() => Base());
                FIRST = false;
                server_url.IsReadOnly = true;
                base_dir.IsReadOnly = true;
                port.IsReadOnly = true;
                user.IsReadOnly = true;
                delay.IsReadOnly = true;
                prepare_button.Visibility = System.Windows.Visibility.Hidden;
                execute_button.Visibility = System.Windows.Visibility.Visible;
            }
            catch (Exception ex)
            {
                WriteLog($"エラーが発生しました。 {ex.Message}");
            }
            prepare_button.IsEnabled = true;
            // iniファイルに設定を書き出し
            IniFile ini = new IniFile("./ftp_check.ini");
            ini["setting", "url"] = URL;
            ini["setting", "port"] = PORT.ToString();
            ini["setting", "user"] = USER;
            ini["setting", "target_dir"] = TARGET_DIR;
            ini["setting", "delay"] = DELAY.ToString();
        }

        private void cancel_button_Click(object sender, RoutedEventArgs e)
        {
            cancel_button.IsEnabled = false;
            WriteLog("待機が終わるまでキャンセルできないのでお待ちいただくかウインドウを閉じてもらっても大丈夫です");
            if (this.cts == null) return;

            cts.Cancel();
        }

        private void clear_button_Click(object sender, RoutedEventArgs e)
        {
            this.Dispatcher.Invoke((Action)(() =>
            {
                log_text.Text = "";
            }));
        }


        private void Notification(string title, string message)
        {
            this.Dispatcher.Invoke((Action)(() =>
            {
                var tmpl = ToastTemplateType.ToastText02;
                var xml = ToastNotificationManager.GetTemplateContent(tmpl);

                var texts = xml.GetElementsByTagName("text");
                texts[0].AppendChild(xml.CreateTextNode(title));
                texts[1].AppendChild(xml.CreateTextNode(message));

                var toast = new ToastNotification(xml);

                ToastNotificationManager.CreateToastNotifier(FTPCheck.Title).Show(toast);
            }));
        }
    }
}
