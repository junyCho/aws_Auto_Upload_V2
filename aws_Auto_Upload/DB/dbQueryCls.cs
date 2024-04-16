using System.Collections.Generic;

namespace aws_Auto_Upload.DB
{
    public static class dbQueryCls
    {      
        public enum eQueryType
        {
            GET_WL,
            GET_WL_REMAIN,
            INS_WL,
            UPT_WL_CNT,
            UPT_WL_COMP,
            DEL_WL_REMAIN
        }

        public static List<Dictionary<string, object>> queryParamArr()
        {
            return new List<Dictionary<string, object>>();
        }

        public static Dictionary<string, object> queryParam()
        {
            return new Dictionary<string, object> { };
        }

        public static string rtnQueryList(eQueryType qType)
        {
            string queryString = string.Empty;
            try
            {
                switch (qType)
                {
                    case eQueryType.GET_WL:
                        queryString = "SELECT FILE_NM FROM WAIT_FILE_LIST WHERE COMP_YN != 'Y'";
                        break;
                    case eQueryType.GET_WL_REMAIN:
                        queryString = "SELECT LINE_CD, FILE_NM, F_DATE, F_TIME, ATMPT_CNT FROM WAIT_FILE_LIST WHERE COMP_YN != 'Y' AND ATMPT_CNT > 1";
                        break;
                    case eQueryType.INS_WL:
                        queryString = "INSERT INTO WAIT_FILE_LIST VALUES({0})";
                        break;
                    case eQueryType.UPT_WL_CNT:
                        queryString = "UPDATE WAIT_FILE_LIST SET ATMPT_CNT = ATMPT_CNT + 1 WHERE FILE_NM = '{0}'";
                        break;
                    case eQueryType.UPT_WL_COMP:
                        queryString = "UPDATE WAIT_FILE_LIST SET COMP_YN = 'Y' WHERE FILE_NM = '{0}'";
                        break;
                    case eQueryType.DEL_WL_REMAIN:
                        queryString = "DELETE FROM WAIT_FILE_LIST WHERE COMP_YN != 'Y'";
                        break;
                    default:
                        break;
                }
                return queryString;
            }
            catch { return queryString; }
        }
    }
}
