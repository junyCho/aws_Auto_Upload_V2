using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using aws_Auto_Upload.DB;
using aws_Auto_Upload.Utils;
using static aws_Auto_Upload.DB.dbQueryCls;

namespace aws_Auto_Upload.Module
{
    public class processBatch
    {
        public processBatch() { }

        public event EventHandler<MessageEventArgs> notiPushEvent;
        
        protected virtual void onNotiPushEvent(string message)
        {
            notiPushEvent?.Invoke(this, new MessageEventArgs(message));
        }

        // 사용자 정의 EventArgs 클래스
        public class MessageEventArgs : EventArgs
        {
            public string Message { get; }
            public MessageEventArgs(string message)
            {
                Message = message;
            }
        }

        // 멤버필드 선언
        private bool _keepThread = false;
        private int interval = 0;
        

        // Thread 변수
        public Thread mThread { get; set; }

        public bool keepThread
        {
            get { return this._keepThread; }
            set { this._keepThread = value; }
        }

        public TaskType _batchType { get; set; }
        

        // 1. S3 Config Setting
        static AmazonS3Config s3Config = new AmazonS3Config
        {
            RegionEndpoint = null,
            ForcePathStyle = true,
            UseHttp = true,
            ServiceURL = settingConfig.s3ServerUrl,
            Timeout = TimeSpan.FromSeconds(10)      // 타임아웃 설정
        };

        // 2. Client Setting
        static BasicAWSCredentials credentials = new BasicAWSCredentials(settingConfig.s3AccessKey, settingConfig.s3SecretKey);
        AmazonS3Client s3Client = new AmazonS3Client(credentials, s3Config);
        

        /// <summary>
        /// Batch 실행 메인
        /// </summary>
        public void batchAuto()
        {
            try
            {
                if (_batchType.Equals(TaskType.Auto))
                    interval = Convert.ToInt32(settingConfig.AutoInterval);
                else if (_batchType.Equals(TaskType.Post))
                    interval = Convert.ToInt32(settingConfig.PostInterval);
            }
            catch (Exception ex)
            {
                interval = 1000 * 60;
                ExCls.Log(logType.error, _batchType, $"[BatchEx] : {ex.Message}");
            }

            loopPoint:
            try
            {
                while (keepThread)
                {
                    // 디렉토리 탐색 (존재하는 파일 모조리 탐색)
                    string rootDir = settingConfig.localRootPath;

                    string[] allFiles = Directory.GetFileSystemEntries(rootDir, "*", SearchOption.AllDirectories);
                    
                    string[] imageFiles = FilterImageFiles(allFiles);

                    // Moving 시킬 파일이 존재한다면, 진행
                    if (imageFiles.Length > 0)
                    {
                        // DB 미처리 리스트 읽어오기
                        DataTable waitingListDt = dbCoreCls.ExecuteSEL(rtnQueryList(eQueryType.GET_WL));

                        // File 정보 읽기 시작
                        setMoveToAWS(imageFiles, waitingListDt);
                    }

                    if (_batchType.Equals(TaskType.Post))
                    {
                        // TASK 1. 후처리 후에 남아 있는 내용을 뿌려준다.
                        //allFiles = Directory.GetFileSystemEntries(rootDir, "*", SearchOption.AllDirectories);
                        //
                        //imageFiles = FilterImageFiles(allFiles);
                        //
                        //ExCls.waitList("waitList", imageFiles, null);
                        //
                        //if (imageFiles.Length > 0)
                        //    onNotiPushEvent($"{imageFiles.Length} 건의 이미지를 전송하지 못했습니다. Log파일을 확인해주세요.");

                        // TASK 2. DB 미처리 내용도 참조값으로 뽑아놓는다.
                        DataTable remainDt = dbCoreCls.ExecuteSEL(rtnQueryList(eQueryType.GET_WL_REMAIN));

                        ExCls.waitList("waitListDB", null, remainDt);

                        if (remainDt.Rows.Count > 0)
                            onNotiPushEvent($"{remainDt.Rows.Count} 건의 이미지를 전송하지 못했습니다. Log파일을 확인해주세요.");
                    }

                    // Task가 끝난 후에 Directory 삭제
                    //removeEmptyDir(rootDir);

                    ExCls.Log(logType.proc, _batchType, "End Task & Wait Next Task");
                    // Thread Interval Setting
                    Thread.Sleep(interval);
                }
            }
            catch (ThreadAbortException e)
            {
                ExCls.Log(logType.error, _batchType, $"[ThreadAbortEx] : {e.Message}");
                Thread.ResetAbort();
            }
            catch (Exception ex)
            {
                ExCls.Log(logType.error, _batchType, $"[BatchEx] : {ex.Message}");
                Thread.Sleep(interval);

                // Loop Point Jump
                goto loopPoint;
            }
        }

        private void setMoveToAWS(string[] fileList, DataTable waitingListDt)
        {
            try
            {
                foreach (string filePath in fileList)
                {
                    FileInfo fi = new FileInfo(filePath);

                    // False 조건 반환시, 수행하지 않음.
                    if (checkWaitingList(fi.Name, waitingListDt))
                    {
                        // 대상 파일을 S3 업로드
                        if (settingConfig.transferType.Equals("0"))
                            mPutObject(filePath, fi.Name);
                        else
                            mCLI(filePath, fi.Name);
                    }
                }
            }
            catch (Exception ex)
            {
                ExCls.Log(logType.error, _batchType, $"[UploadEx] : {ex.Message}");
            }
        }
        
        private void mPutObject(string localFilePath, string fileName)
        {
            try
            {
                // 로컬 파일의 상대 경로를 계산
                string relativePath = localFilePath.Substring(settingConfig.localRootPath.Length);

                // S3에 업로드할 키 설정 (로컬 디렉토리 구조를 그대로 유지)
                string s3Key = relativePath.Replace('\\', '/'); // 경로 구분자를 AWS 스타일로 변경


                PutObjectRequest request = new PutObjectRequest();
                request.BucketName = settingConfig.s3BucketName;
                request.Key = Path.Combine(settingConfig.s3RootPath, s3Key);
                request.FilePath = localFilePath;
                request.UseChunkEncoding = false;
                request.DisableDefaultChecksumValidation = true;
                request.CannedACL = S3CannedACL.BucketOwnerFullControl;

                PutObjectResponse response = s3Client.PutObject(request);
                
                if (response.HttpStatusCode == System.Net.HttpStatusCode.OK)
                {
                    // 성공시, Temp파일 삭제 및 DB처리
                    File.Delete(localFilePath);
                    setWaitList(ResultType.Success, localFilePath);
                    ExCls.Log(logType.proc, _batchType, $"[PUT] Move Complete : {s3Key}");
                }
                else
                {
                    // 실패시, DB처리
                    setWaitList(ResultType.Failure, localFilePath);
                    ExCls.Log(logType.proc, _batchType, $"[PUT] Move Fail : {fileName}");
                }
            }
            catch (AmazonS3Exception ex)
            {
                onNotiPushEvent($"AWS 접속오류! Log파일을 확인해주세요.");
                ExCls.Log(logType.error, _batchType, $"[AWS_PUTEx] : CODE-{ex.ErrorCode} => {ex.Message}");
            }
            catch (Exception ex)
            {
                // 실패시, DB처리
                setWaitList(ResultType.Failure, localFilePath);
                ExCls.Log(logType.error, _batchType, $"[AWS_UnknownEx] : {ex.Message}");
            }
        }

        private void mCLI(string localFilePath, string fileName)
        {
            try
            {
                // 로컬 파일의 상대 경로를 계산
                string relativePath = localFilePath.Substring(settingConfig.localRootPath.Length);

                // S3에 업로드할 키 설정 (로컬 디렉토리 구조를 그대로 유지)
                string s3Key = relativePath.Replace('\\', '/'); // 경로 구분자를 AWS 스타일로 변경

                string httpsOption = string.Empty;

                if (settingConfig.transferType == "2")
                    httpsOption = " --no-verify-ssl";

                // AWS CLI 명령어를 생성
                string awsCliCommand = $"aws s3 mv \"{localFilePath}\" s3://{settingConfig.s3BucketName}/{Path.Combine(settingConfig.s3RootPath, s3Key)} --endpoint-url={settingConfig.s3ServerUrl}{httpsOption}";
                
                // ProcessStartInfo를 설정하여 프로세스 실행
                ProcessStartInfo processStartInfo = new ProcessStartInfo("cmd.exe")
                {
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                
                // 프로세스 실행
                Process process = new Process { StartInfo = processStartInfo };
                process.Start();
                
                // AWS CLI 명령어를 입력 스트림으로 전달
                process.StandardInput.WriteLine(awsCliCommand);
                process.StandardInput.Flush();
                process.StandardInput.Close();
                
                // 결과 출력
                string result = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                
                // 결과 확인
                if (process.ExitCode == 0)
                {
                    // 성공시, Temp파일 삭제 및 DB처리
                    File.Delete(localFilePath);
                    setWaitList(ResultType.Success, localFilePath);
                    ExCls.Log(logType.proc, _batchType, $"[CLI] Move Complete : {s3Key}");
                }
                else
                {
                    // 실패시, DB처리
                    setWaitList(ResultType.Failure, localFilePath);
                    ExCls.Log(logType.proc, _batchType, $"[CLI] Move Fail : {s3Key}");
                }
            }
            catch (AmazonS3Exception ex)
            {
                onNotiPushEvent($"AWS 접속오류! Log파일을 확인해주세요.");
                ExCls.Log(logType.error, _batchType, $"[AWS_CLIEx] : CODE-{ex.ErrorCode} => {ex.Message}");
            }
        }

        private bool checkWaitingList(string fileName, DataTable waitingListDt)
        {
            try
            {
                int exist = waitingListDt.Select($"FILE_NM = '{fileName}'").Length;

                // Auto  :: 정상 전송 프로세스 => Waiting List에 없어야 "True"
                // Post :: 미전송 프로세스 => Waiting List에 있어야 "True"
                if (_batchType.Equals(TaskType.Auto))
                    return exist > 0 ? false : true;
                else if (_batchType.Equals(TaskType.Post))
                    return exist > 0 ? true : false;

                throw new ArgumentException("Not Exists Batch Type.");
            }
            catch { throw; }
        }

        private string[] FilterImageFiles(string[] files)
        {
            try
            {
                string[] imageExtensions = { ".jpg", ".jpeg", ".png", ".gif", ".bmp" }; // Add more extensions if needed

                // Filter files based on image extensions
                return files.Where(file => imageExtensions.Any(ext => file.EndsWith(ext, StringComparison.OrdinalIgnoreCase))).ToArray();
            }
            catch (Exception ex)
            {
                ExCls.Log(logType.error, _batchType, $"[getImageEx] : {ex.Message}");
                return new string[0];
            }
        }

        private void removeEmptyDir(string rootDir)
        {
            try
            {
                string[] allDirectories = Directory.GetDirectories(rootDir, "*", SearchOption.AllDirectories);

                // 각 폴더에 대해 파일 수 확인하여 데이터가 없는 폴더 삭제
                foreach (string directory in allDirectories)
                {
                    // 폴더 내의 파일 수 가져오기
                    int fileCount = Directory.GetFiles(directory).Length;

                    // 파일이 없는 경우 폴더 삭제
                    if (fileCount == 0)
                    {
                        Directory.Delete(directory, true);
                        ExCls.Log(logType.proc, _batchType, $"Deleted empty directory: {directory}");
                    }
                }
            }
            catch (Exception ex)
            {
                ExCls.Log(logType.error, _batchType, $"[UploadEx] : {ex.Message}");
            }
        }

        private void setWaitList(ResultType rType, string fileName)
        {
            try
            {
                // [Auto : 정상 전송]
                // 실패시, 최초 실패이므로 Insert
                if (_batchType.Equals(TaskType.Auto))
                {
                    if (rType.Equals(ResultType.Success)) { }
                    else if (rType.Equals(ResultType.Failure)) 
                    {
                        DateTime dateTime = DateTime.Now;

                        string sql = string.Format(rtnQueryList(eQueryType.INS_WL), "@LINE_CD, @FILE_NM, @F_DATE, @F_TIME, @ATMPT_CNT, @COMP_YN");

                        List<Dictionary<string, object>> param = queryParamArr();
                        param.Add(new Dictionary<string, object> { { "@LINE_CD", settingConfig.lineInfo }, { "@FILE_NM", fileName }, { "@F_DATE", DateTime.Now.ToString("yyyyMMdd") }, { "@F_TIME", DateTime.Now.ToString("HHmmss") }, { "@ATMPT_CNT", 1 }, { "@COMP_YN", "N" } });

                        dbCoreCls.ExecuteINS(sql, param);
                    }
                }
                // [Post : 미처리 전송]
                // 성공시, 처리여부(Y) 업데이트
                // 실패시, 시도 횟수 증가 업데이트
                if (_batchType.Equals(TaskType.Post))
                {
                    if (rType.Equals(ResultType.Success))
                    {
                        string sql = string.Format(rtnQueryList(eQueryType.UPT_WL_COMP), $"{fileName}");
                        dbCoreCls.ExecuteCUD(sql);
                    }
                    else if (rType.Equals(ResultType.Failure))
                    {
                        string sql = string.Format(rtnQueryList(eQueryType.UPT_WL_CNT), $"{fileName}");
                        dbCoreCls.ExecuteCUD(sql);
                    }
                }
            }
            catch (Exception ex)
            {
                ExCls.Log(logType.error, _batchType, $"[setWaitListEx] : {ex.Message}");
            }
        }
    }
}
