using System;
using System.Windows.Forms;
using System.Drawing;
using aws_Auto_Upload.Properties;
using aws_Auto_Upload.Utils;
using System.Diagnostics;
using System.Threading;
using aws_Auto_Upload.Module;
using System.IO;
using System.Runtime.InteropServices;
using aws_Auto_Upload.DB;
using static aws_Auto_Upload.Module.processBatch;
using static aws_Auto_Upload.DB.dbQueryCls;
using System.Runtime.InteropServices.ComTypes;

namespace aws_Auto_Upload
{
    static class Program
    {
        private static NotifyIcon noti = new NotifyIcon();
        //ini파일 경로 지정
        private static string iniPath = string.Empty;           // Ini File Path
        private static processBatch[] procBatch = null;
        private static bool isTooltip = false;

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
        private const int WM_CLOSE = 0x0010;

        /// <summary>
        /// 해당 애플리케이션의 주 진입점입니다.
        /// </summary>
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool bNew = true;
            Mutex mutex = new Mutex(true, "aws_Auto_Upload", out bNew);
            if (!bNew)
            {
                MessageBox.Show("프로그램이 실행중입니다.");
                ExCls.Log(logType.error, TaskType.Normal, "[runEx] : Detect duplicate execution!");

                mutex.ReleaseMutex();
                Application.Exit();
            }
            else
            {
                noti.Text = "AWS Auto Upload";
                noti.Icon = Resources.send_ready;

                noti.ContextMenuStrip = new ContextMenuStrip();
                noti.ContextMenuStrip.Items.Add("AWS Auto Upload", null, null);
                noti.ContextMenuStrip.Items.Add(new ToolStripSeparator());
                noti.ContextMenuStrip.Items.Add("실행상태", Resources.connect, Status_Clicked);
                noti.ContextMenuStrip.Items.Add("설정", Resources.setting, Setting_Open_Clicked);
                noti.ContextMenuStrip.Items.Add("로그", Resources.help, Log_Clicked);
                noti.ContextMenuStrip.Items.Add("미처리 초기화", Resources.setting, Waiting_Clicked);
                noti.ContextMenuStrip.Items.Add(new ToolStripSeparator());
                noti.ContextMenuStrip.Items.Add("종료", null, Exit_Clicked);
            
                noti.Visible = true;
                noti.BalloonTipClosed += Noti_BalloonTipClosed;

                AwsAutoRun();
                               
                Application.Run();

                mutex.ReleaseMutex();
            }
        }

        private static void Noti_BalloonTipClosed(object sender, EventArgs e)
        {
            isTooltip = false;
        }

        private static void DbInitial()
        {
            dbCoreCls.dbIstsNm = settingConfig.dbName;
            dbCoreCls.dbPath = Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath);
            dbCoreCls.dbFileDir = String.Format(@"{0}\{1}", dbCoreCls.dbPath, "Database");
            dbCoreCls.dbFilePath = String.Format(@"{0}\{1}", dbCoreCls.dbFileDir, dbCoreCls.dbIstsNm);

            dbCoreCls.createDb();
            dbCoreCls.createTable();
        }

        /// <summary>
        /// 최초 실행시, 연결 가능하다면 자동 연결
        /// </summary>
        private static void AwsAutoRun()
        {
            try
            {
                procBatch = new processBatch[2];

                if (configCheck())
                {
                    DbInitial();
                    activeUpload();
                }

                runTimeCheck();
            }
            catch (Exception ex) 
            {
                ExCls.Log(logType.error, TaskType.Normal, $"[runEx] : {ex.Message}");
            }
        }

        /// <summary>
        /// Ini 설정파일 읽어오기
        /// </summary>
        private static bool configCheck()
        {
            try
            {
                iniPath = Application.StartupPath + @"\config.ini";

                if (!new FileInfo(iniPath).Exists)
                {
                    using(StreamWriter sw = new StreamWriter(iniPath))
                    {
                        sw.Flush();
                        sw.Close();
                    }
                    
                    IniFile.SetValue(iniPath, "S3_Info", "ACCESS_KEY", "NULL_KEY");            // S3 AccessKey
                    IniFile.SetValue(iniPath, "S3_Info", "SECRET_KEY", "NULL_SKEY");           // S3 SecretKey
                    IniFile.SetValue(iniPath, "S3_Info", "BUCKET_NAME", "BUCKET");             // S3 BucketName
                    IniFile.SetValue(iniPath, "S3_Info", "SERVER_IP", "127.0.0.1");            // S3 Server IP
                    IniFile.SetValue(iniPath, "S3_Info", "SERVER_PORT", "1004");               // S3 Server Port
                    
                    IniFile.SetValue(iniPath, "Path_Info", "LOCAL_ROOT_PATH", @"C:\Users\USER");// Local Root Path
                    IniFile.SetValue(iniPath, "Path_Info", "S3_ROOT_PATH", @"C:\Users\USER");   // AWS Root Path
                    IniFile.SetValue(iniPath, "Path_Info", "LOG_PATH", @"C:\Users\USER\log");   // Log Path

                    IniFile.SetValue(iniPath, "Process_Info", "DB_NM", "sqlLite_AWS.db");       // DB Name
                    IniFile.SetValue(iniPath, "Process_Info", "LINE_INFO", "1");                // Line Info
                    IniFile.SetValue(iniPath, "Process_Info", "TRANSFER_TYPE", "0");            // 전송방법 (0:PutObject / 1:CLI)
                    IniFile.SetValue(iniPath, "Process_Info", "AUTO_INTERVAL", "10000");        // 정상업로드 Interval
                    IniFile.SetValue(iniPath, "Process_Info", "POST_INTERVAL", "600000");       // 미처리업로드 Interval
                }

                settingConfig.s3AccessKey = IniFile.GetValue(iniPath, "S3_Info", "ACCESS_KEY", "%");            // S3 AccessKey
                settingConfig.s3SecretKey = IniFile.GetValue(iniPath, "S3_Info", "SECRET_KEY", "%");            // S3 SecretKey
                settingConfig.s3BucketName = IniFile.GetValue(iniPath, "S3_Info", "BUCKET_NAME", "%");          // S3 BucketName
                settingConfig.s3ServerIp = IniFile.GetValue(iniPath, "S3_Info", "SERVER_IP", "%");              // S3 Server IP
                settingConfig.s3ServerPort = IniFile.GetValue(iniPath, "S3_Info", "SERVER_PORT", "%");          // S3 Server Port
                settingConfig.s3ServerUrl = $"http://{settingConfig.s3ServerIp}:{settingConfig.s3ServerPort}";  // URL

                settingConfig.localRootPath = IniFile.GetValue(iniPath, "Path_Info", "LOCAL_ROOT_PATH", "%");   // Local Root Path
                settingConfig.s3RootPath = IniFile.GetValue(iniPath, "Path_Info", "S3_ROOT_PATH", "%");         // AWS Root Path
                settingConfig.logPath = IniFile.GetValue(iniPath, "Path_Info", "LOG_PATH", "%");                // Log Path

                settingConfig.dbName = IniFile.GetValue(iniPath, "Process_Info", "DB_NM", "%");                  // DB Name
                settingConfig.lineInfo = IniFile.GetValue(iniPath, "Process_Info", "LINE_INFO", "%");            // Line Info
                settingConfig.transferType = IniFile.GetValue(iniPath, "Process_Info", "TRANSFER_TYPE", "%");    // 전송방법 (0:PutObject / 1:CLI)
                settingConfig.AutoInterval = IniFile.GetValue(iniPath, "Process_Info", "AUTO_INTERVAL", "%");    // 정상업로드 Interval
                settingConfig.PostInterval = IniFile.GetValue(iniPath, "Process_Info", "POST_INTERVAL", "%");    // 미처리업로드 Interval

                if (settingConfig.s3ServerUrl.Equals("%") || settingConfig.s3SecretKey.Equals("%")  || settingConfig.s3BucketName.Equals("%") ||
                    settingConfig.s3ServerIp.Equals("%")  || settingConfig.s3ServerPort.Equals("%") || settingConfig.s3ServerUrl.Equals("%") ||
                    settingConfig.s3ServerIp.Equals("%")  || settingConfig.s3ServerPort.Equals("%") || settingConfig.logPath.Equals("%") ||
                    settingConfig.dbName.Equals("%") || settingConfig.lineInfo.Equals("%") || settingConfig.transferType.Equals("%") || settingConfig.AutoInterval.Equals("%") || settingConfig.PostInterval.Equals("%"))
                {
                    ExCls.Log(logType.error, TaskType.Normal, $"[configEx] : ini setting load Error!. Empty or Not Exist Factor.");

                    return false;
                }
                else
                    return true;
            }
            catch (Exception ex) 
            {
                throw;
            }
        }

        /// <summary>
        /// 업로드 프로세스 활성
        /// </summary>
        private static void activeUpload()
        {
            try
            {
                // Thread 실행
                // 2중 실행 (정상 전송용, 오류 처리용)

                if (procBatch[0] == null)
                {
                    //=======================================
                    // Task 1 :: Auto Upload (자동 업로드) 
                    //=======================================
                    procBatch[0] = new processBatch();
                    procBatch[0].notiPushEvent += post_notiPushEvent;
                    procBatch[0]._batchType = TaskType.Auto;
                    procBatch[0].keepThread = true;                                             // Thread Flag 시작
                    procBatch[0].mThread = new Thread(() => procBatch[0].batchAuto());          // Threading 대상 함수
                    procBatch[0].mThread.Name = "Auto";                                         // Thread Naming
                    procBatch[0].mThread.Start();                                               // Thread Start

                    ExCls.Log(logType.proc, TaskType.Normal, $"[run] : Auto Thread Run Complete!.");
                }
                else
                    ExCls.Log(logType.proc, TaskType.Normal, $"[run] : Auto Thread Already Run!.");

                // Sleep 2초정도 준다. 파일 동시 잡는거 한번 걸러주기 위해.
                Thread.Sleep(2000);
                
                if (procBatch[1] == null)
                {
                    //=======================================
                    // Task 2 :: post Upload (미처리(후처리) 업로드) 
                    //=======================================
                    procBatch[1] = new processBatch();
                    procBatch[1].notiPushEvent += post_notiPushEvent;
                    procBatch[1]._batchType = TaskType.Post;
                    procBatch[1].keepThread = true;                                             // Thread Flag 시작
                    procBatch[1].mThread = new Thread(() => procBatch[1].batchAuto());          // Threading 대상 함수
                    procBatch[1].mThread.Name = "Post";                                         // Thread Naming
                    procBatch[1].mThread.Start();                                               // Thread Start
                
                    ExCls.Log(logType.proc, TaskType.Normal, $"[run] : Post Thread Run Complete!.");
                }
                else
                    ExCls.Log(logType.proc, TaskType.Normal, $"[run] : Post Thread Already Run!.");
            }
            catch (Exception ex) 
            {
                ExCls.Log(logType.error, TaskType.Normal, $"[threadEx] : {ex.Message}");
            }
        }

        private static void post_notiPushEvent(object sender, MessageEventArgs e)
        {
            try
            {
                isTooltip = true;
                noti.ShowBalloonTip(100, "오류 알림", $"연결문제 혹은 AWS 점검 필요[{e.Message}]", ToolTipIcon.Error);
            }
            catch (Exception ex)
            {
                ExCls.Log(logType.error, TaskType.Normal, $"[NotiEx] : {ex.Message}");
            }
        }

        /// <summary>
        /// 업로드 프로세스 비활성
        /// </summary>
        private static void deactiveUpload()
        {
            try
            {
                // Thread 실행
                // 2중 실행 (정상 전송용, 오류 처리용)
                for (int i = 0; i < procBatch.Length; i++)
                {
                    if (procBatch[i] != null)
                    {
                        procBatch[i].keepThread = false;
                        procBatch[i].mThread.Abort();
                        procBatch[i].mThread = null;

                        procBatch[i] = null;

                        ExCls.Log(logType.proc, TaskType.Normal, $"[run] : {i}번째 Thread Stop Complete!.");
                    }
                    else
                    {
                        ExCls.Log(logType.proc, TaskType.Normal, $"[run] : {i}번째 Thread Already Stopped!.");
                    }
                }
            }
            catch (Exception ex)
            {
                ExCls.Log(logType.error, TaskType.Normal, $"[threadEx] : {ex.Message}");
            }
        }

        /// <summary>
        /// ToolStrip 상태 값 변경
        /// </summary>
        private static void runTimeCheck()
        {
            try
            {
                if (inRunning())
                {
                    (noti.ContextMenuStrip.Items[0] as ToolStripItem).BackColor = Color.FromArgb(120, 55, 138, 44);
                    (noti.ContextMenuStrip.Items[0] as ToolStripItem).Text = "AWS Auto Upload [Running] - V1.0.0";
                    (noti.ContextMenuStrip.Items[0] as ToolStripItem).ForeColor = Color.DimGray;

                    (noti.ContextMenuStrip.Items[2] as ToolStripItem).Text = "중지";
                    (noti.ContextMenuStrip.Items[2] as ToolStripItem).Tag = "Running";
                    (noti.ContextMenuStrip.Items[2] as ToolStripItem).Image = Resources.disconnect;
                    noti.Icon = Resources.send_active;
                }
                else
                {
                    (noti.ContextMenuStrip.Items[0] as ToolStripItem).BackColor = Color.FromArgb(120, 138, 55, 44);
                    (noti.ContextMenuStrip.Items[0] as ToolStripItem).Text = "AWS Auto Upload [Stopped] - V1.0.0";
                    (noti.ContextMenuStrip.Items[0] as ToolStripItem).ForeColor = Color.WhiteSmoke;

                    (noti.ContextMenuStrip.Items[2] as ToolStripItem).Text = "실행";
                    (noti.ContextMenuStrip.Items[2] as ToolStripItem).Tag = "Stopped";
                    (noti.ContextMenuStrip.Items[2] as ToolStripItem).Image = Resources.connect;
                    noti.Icon = Resources.send_deactive;
                }
            }
            catch { throw; }
        }

        private static bool inRunning()
        {
            try
            {
                if (procBatch != null)
                {
                    for (int i = 0; i < procBatch.Length; i++)
                    {
                        if (procBatch[i] == null)
                            return false;

                        if (!(procBatch[i].keepThread && procBatch[i].mThread != null))
                            return false;

                        if (!procBatch[i].mThread.IsAlive)
                            return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                ExCls.Log(logType.error, TaskType.Normal, $"[isRunCheckEx] : {ex.Message}");
                return false;
            }
        }
        /// <summary>
        /// 연결 상태 클릭 이벤트 ( 연결 또는 연결헤제)
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private static void Status_Clicked(object sender, EventArgs e)
        {
            try
            {
                if ((noti.ContextMenuStrip.Items[2] as ToolStripItem).Tag.Equals("Running"))
                {
                    if (configCheck())
                        deactiveUpload();
                }
                    
                else if ((noti.ContextMenuStrip.Items[2] as ToolStripItem).Tag.Equals("Stopped"))
                {
                    if (configCheck())
                        activeUpload();
                }
                else
                    throw new ArgumentException("status click Error.");

                runTimeCheck();
            }
            catch (Exception ex) { ExCls.Log(logType.error, TaskType.Normal, $"[statusEx] : {ex.Message}"); }
        }

        /// <summary>
        /// 설정 Open 이벤트
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private static void Setting_Open_Clicked(object sender, EventArgs e)
        {
            try
            {
                if (iniPath != null)
                    Process.Start(iniPath);
            }
            catch (Exception ex) { ExCls.Log(logType.error, TaskType.Normal, $"[openEx] : {ex.Message}"); }
        }

        /// <summary>
        /// 로그 이벤트
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private static void Log_Clicked(object sender, EventArgs e)
        {
            try
            {
                if (!settingConfig.logPath.Equals("%"))
                {
                    if (new DirectoryInfo(settingConfig.logPath).Exists)
                        Process.Start(settingConfig.logPath);
                }
            }
            catch (Exception ex) { ExCls.Log(logType.error, TaskType.Normal, $"[NotiEx] : {ex.Message}"); }
        }

        private static void Waiting_Clicked(object sender, EventArgs e)
        {
            try
            {
                dbCoreCls.ExecuteCUD(rtnQueryList(eQueryType.DEL_WL_REMAIN));
            }
            catch (Exception ex) { ExCls.Log(logType.error, TaskType.Normal, $"[NotiEx] : {ex.Message}"); }
        }

        /// <summary>
        /// 종료 이벤트
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private static void Exit_Clicked(object sender, EventArgs e)
        {
            if ((noti.ContextMenuStrip.Items[2] as ToolStripItem).Tag.Equals("Running"))
                deactiveUpload();

            Environment.Exit(0);
        }
    }
}
