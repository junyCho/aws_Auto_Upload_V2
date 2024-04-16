using aws_Auto_Upload.Module;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace aws_Auto_Upload.Utils
{
    public enum TaskType
    {
        Normal,
        Auto,
        Post,
        Db
    }

    public enum logType
    {
        proc,
        error
    }

    public enum ResultType
    {
        Success,
        Failure
    }

    class ExCls
    {
        public static void Log(logType _logType, TaskType _batchType, string message)
        {
            DateTime dt = DateTime.Now;
            string year = dt.Year.ToString();
            string month = dt.Month.ToString("0#");
            string day = dt.Day.ToString("0#");

            string targetDirPath = Path.Combine(settingConfig.logPath, year, month);

            // 디렉터리 생성
            if (!new DirectoryInfo(targetDirPath).Exists)
                Directory.CreateDirectory(targetDirPath);

            string logFilePath = Path.Combine(targetDirPath, $"{day}_{_logType.ToString()}_{_batchType.ToString()}.log");

            if (!new FileInfo(logFilePath).Exists)
            {
                using (StreamWriter sw = new StreamWriter(logFilePath))
                {
                    sw.Flush();
                    sw.Close();
                }
            }

            // Append the log entry to the file
            using (StreamWriter writer = File.AppendText(logFilePath))
            {
                writer.WriteLine($"{DateTime.Now.ToString("yyyyMMdd HH:mm:ss.fff")} - {message}");
            }
        }

        public static void waitList(string logName, string[] fileList, DataTable dt)
        {
            string targetDirPath = Path.Combine(settingConfig.logPath);

            // 디렉터리 생성
            if (!new DirectoryInfo(targetDirPath).Exists)
                Directory.CreateDirectory(targetDirPath);

            string logFilePath = Path.Combine(targetDirPath, $"{logName}.log");

            if (!new FileInfo(logFilePath).Exists)
            {
                using (StreamWriter sw = new StreamWriter(logFilePath))
                {
                    sw.Flush();
                    sw.Close();
                }
            }

            // 내용 덮어쓰기
            string content = "";

            if (fileList != null)
            {
                foreach (string file in fileList)
                {
                    content += $"{file}\n";
                }
            }
            else if (dt != null)
            {
                foreach (DataRow row in dt.Rows)
                {
                    content += $"{row["FILE_NM"]} ({row["ATMPT_CNT"]}) - {row["F_DATE"]}:{row["F_TIME"]}\n";
                }
            }

            File.WriteAllText(logFilePath, content);
        }
    }

    class IniFile
    {
        [DllImport("kernel32")]
        private static extern long WritePrivateProfileString(string section, string key, string val, string filePath);
        [DllImport("kernel32")]
        private static extern int GetPrivateProfileString(string section, string key, string def, StringBuilder retVal, int size, string filePath);

        public static void SetValue(string path, string Section, string Key, string Value)
        {
            WritePrivateProfileString(Section, Key, Value, path);
        }

        public static string GetValue(string path, string Section, string Key, string Default)
        {
            StringBuilder temp = new StringBuilder(255);
            int i = GetPrivateProfileString(Section, Key, Default, temp, 255, path);
            if (temp != null && temp.Length > 0) return temp.ToString();
            else return Default;
        }
    }
}
