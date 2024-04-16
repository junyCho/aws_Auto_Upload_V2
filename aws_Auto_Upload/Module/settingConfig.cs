
namespace aws_Auto_Upload.Module
{
    public class settingConfig
    {
        public static string s3AccessKey { get; set; }      // S3 AccessKey
        public static string s3SecretKey { get; set; }      // S3 SecretKey
        public static string s3BucketName { get; set; }     // S3 BucketName
        public static string s3ServerIp { get; set; }       // S3 Server IP
        public static string s3ServerPort { get; set; }     // S3 Server Port
        public static string s3ServerUrl { get; set; }      // AWS Service Url
        public static string localRootPath { get; set; }    // Local Root Path
        public static string s3RootPath { get; set; }       // AWS Root Path
        public static string logPath { get; set; }          // Log Path

        public static string dbName { get; set; }           // db name
        public static string lineInfo { get; set; }         // Line Info
        public static string transferType { get; set; }     // 전송 방법
        public static string AutoInterval { get; set; }     // 정상업로드 Interval
        public static string PostInterval { get; set; }     // 미처리업로드 Interval
    }
}
