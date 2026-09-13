using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

[assembly: AssemblyTitle("DemoMapDB")]
[assembly: AssemblyProduct("DemoMapDB - 近隣店舗の検索")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace StoreMapDemo
{
    static class Program
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern uint GetConsoleProcessList(uint[] processList, uint processCount);
        [DllImport("kernel32.dll")]
        static extern bool FreeConsole();
        [DllImport("kernel32.dll")]
        static extern IntPtr GetConsoleWindow();
        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [STAThread]
        static int Main(string[] args)
        {
            // 引数あり → コマンドラインモード（標準出力に結果を書いて終了）
            if (args.Length > 0) return Cli.Run(args);

            // 引数なし → GUI。ダブルクリック起動で自分用に作られたコンソール窓は閉じる
            DetachOwnConsole();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += (s, e) =>
                MessageBox.Show("エラーが発生しました。\r\n\r\n" + e.Exception.Message, "DemoMapDB",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);

            StoreStore store;
            string path = StoreStore.ResolveDefaultPath();
            try { store = new StoreStore(path); }
            catch (Exception ex)
            {
                MessageBox.Show("データファイルを読み込めませんでした。\r\n" + path + "\r\n\r\n" + ex.Message +
                    "\r\n\r\n同じフォルダの " + StoreStore.FileName + ".bak（直前の版）から復元できます。",
                    "DemoMapDB", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return Cli.ExitError;
            }
            Settings.Load(store.FilePath);
            Theme.LoadSetting(store.FilePath);

            // はじめて起動したときは、すぐ試せるようにサンプル店舗を入れる
            if (store.Data.Stores.Count == 0)
            {
                store.Data.Seed();
                try { store.Save(); } catch { }
            }

            Application.Run(new MainForm(store));
            return 0;
        }

        static void DetachOwnConsole()
        {
            try
            {
                var list = new uint[4];
                if (GetConsoleProcessList(list, (uint)list.Length) <= 1)
                {
                    IntPtr h = GetConsoleWindow();
                    if (h != IntPtr.Zero) ShowWindow(h, 0);
                    FreeConsole();
                }
            }
            catch { }
        }
    }
}
