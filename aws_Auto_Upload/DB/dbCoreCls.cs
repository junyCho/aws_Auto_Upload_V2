using Amazon.S3.Model.Internal.MarshallTransformations;
using aws_Auto_Upload.Utils;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;

namespace aws_Auto_Upload.DB
{
    public class dbCoreCls
    {
        public static string dbIstsNm;
        public static string dbPath;
        public static string dbFileDir;
        public static string dbFilePath;
        private static DataTable blankDt;

        public enum dbCallType
        {
            CUD,
            SEL,
            INS,
            INS_DT
        }

        /// <summary>
        /// DB 생성 메서드
        /// </summary>
        public static void createDb()
        {
            try
            {
                string conString = string.Format("Data Source={0};", dbFilePath);

                if (!System.IO.Directory.Exists(dbFileDir))
                {
                    System.IO.Directory.CreateDirectory(dbFileDir);
                    ExCls.Log(logType.proc, TaskType.Db, $"[DataBase] Create Directory : {dbFileDir}");
                }
                else
                    ExCls.Log(logType.proc, TaskType.Db, $"[DataBase] Create Directory : {dbFileDir} --> 이미 존재함.");
                if (!System.IO.File.Exists(dbFilePath))
                {
                    SQLiteConnection.CreateFile(dbFilePath);
                    ExCls.Log(logType.proc, TaskType.Db, $"[DataBase] Create DataBase : {dbFilePath}");
                }
                else
                    ExCls.Log(logType.proc, TaskType.Db, $"[DataBase] Create DataBase : {dbFilePath} --> 이미 존재함.");
            }
            catch (Exception ex) { ExCls.Log(logType.error, TaskType.Db, ex.Message); }
        }

        /// <summary>
        /// 테이블 생성 메서드
        /// </summary>
        public static void createTable()
        {
            try
            {
                DataTable rtnDt = new DataTable();
                string query = string.Empty;

                //===========================
                // Task 1
                //===========================
                rtnDt = ExecuteSEL("SELECT name FROM sqlite_master WHERE type = 'table' AND name = 'WAIT_FILE_LIST';");

                // ** 미처리 Table 생성
                if (rtnDt.Rows.Count == 0)
                {
                    query = "CREATE TABLE WAIT_FILE_LIST (LINE_CD VARCHAR(20), FILE_NM VARCHAR(500), F_DATE VARCHAR(30), F_TIME VARCHAR(30),  ATMPT_CNT INTEGER, COMP_YN VARCHAR(1));";

                    ExecuteCUD(query);

                    ExCls.Log(logType.proc, TaskType.Db, "[TABLE 생성] NP_FILE_LIST");
                }
                else
                    ExCls.Log(logType.proc, TaskType.Db, "[TABLE 생성] NP_FILE_LIST --> 이미 존재함!.");
            }
            catch (Exception ex) { ExCls.Log(logType.error, TaskType.Db, ex.Message); }
        }

        public static DataTable ExecuteSEL(string commandText)
        {
            ExecuteQuery(dbCoreCls.dbCallType.SEL, commandText, null, null, out blankDt);

            return blankDt;
        }

        public static void ExecuteCUD(string commandText)
        {
            ExecuteQuery(dbCoreCls.dbCallType.CUD, commandText, null, null, out blankDt);
        }

        public static void ExecuteINS(string commandText, List<Dictionary<string, object>> queryParamSet)
        {
            ExecuteQuery(dbCoreCls.dbCallType.INS, commandText, null, queryParamSet, out blankDt);
        }

        public static void ExecuteINS_DT(string commandText, DataTable dt)
        {
            ExecuteQuery(dbCoreCls.dbCallType.INS, commandText, dt, null, out blankDt);
        }

        public static void ExecuteQuery(dbCallType callType, string commandText, DataTable inDt, List<Dictionary<string, object>> queryParamSet, out DataTable dt)
        {
            SQLiteConnection connection = new SQLiteConnection();

            connection.ConnectionString = string.Format("Data Source={0};", dbFilePath);
            connection.Open();
            SQLiteCommand command = new SQLiteCommand(connection);

            dt = new DataTable();

            try
            {
                command.CommandText = commandText;

                if (callType.Equals(dbCallType.SEL))
                {
                    SQLiteDataAdapter adapter = new SQLiteDataAdapter(command);
                    adapter.AcceptChangesDuringFill = false;
                    adapter.Fill(dt);
                }
                else if (callType.Equals(dbCallType.INS))
                {
                    BeginTran(connection);

                    try
                    {
                        foreach (var queryParam in queryParamSet)
                        {
                            foreach (var param in queryParam)
                            {
                                command.Parameters.AddWithValue($"{param.Key}", param.Value);
                            }
                            command.ExecuteNonQuery();
                        }

                        CommitTran(connection);
                    }
                    catch (Exception ex) { ExCls.Log(logType.error, TaskType.Db, $"[ExecuteInsrt] 데이터 수행 오류 : {ex.Message}"); RollbackTran(connection); throw; }
                }
                else if (callType.Equals(dbCallType.INS_DT))
                {
                    BeginTran(connection);

                    try
                    {
                        for (int i = 0; i < inDt.Rows.Count; i++)
                        {
                            for (int j = 0; j < inDt.Columns.Count; j++)
                            {
                                command.Parameters.AddWithValue($"@{inDt.Columns[j].ColumnName}", inDt.Rows[i][inDt.Columns[j].ColumnName]);
                            }
                            command.ExecuteNonQuery();
                        }

                        CommitTran(connection);
                    }
                    catch (Exception ex) { ExCls.Log(logType.error, TaskType.Db, $"[ExecuteInsrt] 데이터 수행 오류 : {ex.Message}"); RollbackTran(connection); throw; }
                }
                else
                {
                    command.ExecuteNonQuery();
                }
            }
            catch { }
            finally 
            {
                command.Dispose();
                connection.Close();
                connection.Dispose();
            }
        }

        //트랜잭션 시작
        private static void BeginTran(SQLiteConnection conn)
        {
            SQLiteCommand command = new SQLiteCommand("Begin", conn);
            command.ExecuteNonQuery();
            command.Dispose();
        }

        //트랜잭션 완료
        private static void CommitTran(SQLiteConnection conn)
        {
            SQLiteCommand command = new SQLiteCommand("Commit", conn);
            command.ExecuteNonQuery();
            command.Dispose();
        }

        private static void RollbackTran(SQLiteConnection conn)
        {
            SQLiteCommand command = new SQLiteCommand("Rollback", conn);
            command.ExecuteNonQuery();
            command.Dispose();
        }
    }
}
