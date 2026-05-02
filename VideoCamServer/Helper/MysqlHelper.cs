//using Microsoft.Extensions.Logging;
//using MySql.Data.MySqlClient;
//using System;
//using System.Collections.Generic;
//using System.Data;
//using System.Data.SqlClient;

//namespace VideoCamServer.Helper
//{
//    /// <summary>
//    /// 数据库操作类
//    /// </summary>
//    public class MysqlHelper
//    {
//        public MySqlConnection conn;
       
//        public static  string _connStr;

       
//        public static  int ExecuteNonQuery(CommandType cmdType, string cmdText, params MySqlParameter[] cmdParameters)
//        {

//            return ExecuteNonQuery(cmdText, cmdType,false, cmdParameters);
//        }

//        /// <summary> 
//        /// 给定连接的数据库用假设参数执行一个sql命令（不返回数据集） 
//        /// </summary> 
//        /// <param name="connectionString">一个有效的连接字符串</param> 
//        /// <param name="cmdType">命令类型(存储过程, 文本, 等等)</param> 
//        /// <param name="cmdText">存储过程名称或者sql命令语句</param> 
//        /// <param name="commandParameters">执行命令所用参数的集合</param> 
//        /// <returns>执行命令所影响的行数</returns> 
//        public static int ExecuteNonQuery( string cmdText,CommandType commandType= CommandType.Text, bool isTran=false, params MySqlParameter[] cmdParameters)
//        {
//            int res = -1;
//            int i = 1;
//            while (i < 6 && res == -1)
//            {
//                MySqlConnection conn = new MySqlConnection(_connStr);
//                MySqlCommand cmd = new MySqlCommand();
//                try
//                {
//                    //DateTime beginDateTime = DateTime.Now;
//                    PrepareCommand(cmd, conn, isTran, commandType, cmdText, cmdParameters);
//                    //DateTime beginOpenDateTime = DateTime.Now;
//                    //logger.LogDebug("ExecSQL打开数据库连接 " + cmdText + "耗时" + ":" + (beginOpenDateTime - beginDateTime).TotalMilliseconds);
//                    res = cmd.ExecuteNonQuery();
//                    cmd.Parameters.Clear();
                    
//                    if (isTran)
//                        cmd.Transaction.Commit();
//                    //DateTime endDateTime = DateTime.Now;
//                    //logger.LogDebug("ExecSQL " + cmdText + "耗时" + ":" + (endDateTime - beginDateTime).TotalMilliseconds);
//                }
//                catch (Exception ex)
//                {
//                    if (ex.Message.StartsWith("Duplicate entry"))
//                    {
//                        res = 1;
//                    }
//                    LogHelper.WriteLog("mysql", "mysqlExecSQL " + cmdText + "执行次数" + i+"" +ex.StackTrace);

//                    if (isTran)
//                        cmd.Transaction.Rollback();
//                }
//                finally
//                {
//                    if (cmd.Connection.State == ConnectionState.Open)
//                    {
//                        cmd.Connection.Close();
//                    }
//                    cmd.Dispose();
//                }
//                i++;

//            }
//            return res;
//        }


//        ///// <summary> 
//        ///// 用现有的数据库连接执行一个sql命令（不返回数据集） 
//        ///// </summary> 
//        ///// <param name="connection">一个现有的数据库连接</param> 
//        ///// <param name="cmdType">命令类型(存储过程, 文本, 等等)</param> 
//        ///// <param name="cmdText">存储过程名称或者sql命令语句</param> 
//        ///// <param name="commandParameters">执行命令所用参数的集合</param> 
//        ///// <returns>执行命令所影响的行数</returns> 
//        //public static int ExecuteNonQuery(MySqlConnection connection, CommandType cmdType, string cmdText, params MySqlParameter[] commandParameters)
//        //{
//        //    MySqlCommand cmd = new MySqlCommand();
//        //    PrepareCommand(cmd, connection, null, cmdType, cmdText, commandParameters);
//        //    int val = cmd.ExecuteNonQuery();
//        //    cmd.Parameters.Clear();
//        //    return val;
//        //}

//        ///// <summary> 
//        /////使用现有的SQL事务执行一个sql命令（不返回数据集） 
//        ///// </summary> 
//        ///// <remarks> 
//        /////举例: 
//        ///// int result = ExecuteNonQuery(connString, CommandType.StoredProcedure, "PublishOrders", new MySqlParameter("@prodid", 24)); 
//        ///// </remarks> 
//        ///// <param name="trans">一个现有的事务</param> 
//        ///// <param name="cmdType">命令类型(存储过程, 文本, 等等)</param> 
//        ///// <param name="cmdText">存储过程名称或者sql命令语句</param> 
//        ///// <param name="commandParameters">执行命令所用参数的集合</param> 
//        ///// <returns>执行命令所影响的行数</returns> 
//        //public static int ExecuteNonQuery(MySqlTransaction trans, CommandType cmdType, string cmdText, params MySqlParameter[] commandParameters)
//        //{
//        //    MySqlCommand cmd = new MySqlCommand();
//        //    PrepareCommand(cmd, trans.Connection, trans, cmdType, cmdText, commandParameters);
//        //    int val = cmd.ExecuteNonQuery();
//        //    cmd.Parameters.Clear();
//        //    return val;
//        //}

//        ///// <summary> 
//        ///// 用执行的数据库连接执行一个返回数据集的sql命令 
//        ///// </summary> 
//        ///// <remarks> 
//        ///// 举例: 
//        ///// MySqlDataReader r = ExecuteReader(connString, CommandType.StoredProcedure, "PublishOrders", new MySqlParameter("@prodid", 24)); 
//        ///// </remarks> 
//        ///// <param name="connectionString">一个有效的连接字符串</param> 
//        ///// <param name="cmdType">命令类型(存储过程, 文本, 等等)</param> 
//        ///// <param name="cmdText">存储过程名称或者sql命令语句</param> 
//        ///// <param name="commandParameters">执行命令所用参数的集合</param> 
//        ///// <returns>包含结果的读取器</returns> 
//        //public static MySqlDataReader ExecuteReader(string connectionString, CommandType cmdType, string cmdText, params MySqlParameter[] commandParameters)
//        //{
//        //    MySqlCommand cmd = new MySqlCommand();
//        //    MySqlConnection conn = new MySqlConnection(connectionString);
//        //    try
//        //    {
//        //        PrepareCommand(cmd, conn, null, cmdType, cmdText, commandParameters);
//        //        MySqlDataReader reader = cmd.ExecuteReader(CommandBehavior.CloseConnection);
//        //        cmd.Parameters.Clear();
//        //        return reader;
//        //    }
//        //    catch
//        //    {
//        //        conn.Close();
//        //        throw;
//        //    }
//        //}
//        /// <summary> 
//        /// 返回DataSet 
//        /// </summary> 
//        /// <param name="connectionString">一个有效的连接字符串</param> 
//        /// <param name="cmdType">命令类型(存储过程, 文本, 等等)</param> 
//        /// <param name="cmdText">存储过程名称或者sql命令语句</param> 
//        /// <param name="commandParameters">执行命令所用参数的集合</param> 
//        /// <returns></returns> 
//        public static DataSet ExecuteDataSet(CommandType cmdType, string cmdText, params MySqlParameter[] cmdParameters)
//        {
//            return GetDataSet( CommandType.Text, cmdText, false, null);
//        }
//        /// <summary> 
//        /// 返回DataSet 
//        /// </summary> 
//        /// <param name="connectionString">一个有效的连接字符串</param> 
//        /// <param name="cmdType">命令类型(存储过程, 文本, 等等)</param> 
//        /// <param name="cmdText">存储过程名称或者sql命令语句</param> 
//        /// <param name="commandParameters">执行命令所用参数的集合</param> 
//        /// <returns></returns> 
//        public static DataSet GetDataSet( CommandType cmdType, string cmdText, bool isTran, params MySqlParameter[] commandParameters)
//        {
//            DataSet ds = null;
//            MySqlCommand cmd = new MySqlCommand();
//            MySqlConnection conn = new MySqlConnection(_connStr);
//            try
//            {
//                //DateTime beginDateTime = DateTime.Now;
//                PrepareCommand(cmd, conn, isTran, cmdType, cmdText, commandParameters);
//                //DateTime beginOpenDateTime = DateTime.Now;
//                //logger.LogDebug("mysqlGetDataSet打开数据库连接 " + cmdText + "耗时" + ":" + (beginOpenDateTime - beginDateTime).TotalMilliseconds);

//                MySqlDataAdapter adapter = new MySqlDataAdapter();
//                adapter.SelectCommand = cmd;
//                ds = new DataSet();

//                adapter.Fill(ds);
//                cmd.Parameters.Clear();
//                conn.Close();
//                //DateTime endDateTime = DateTime.Now;
//                //logger.LogDebug("mysqlGetDataSet " + cmdText + "耗时" + ":" + (endDateTime - beginDateTime).TotalMilliseconds);
//            }
//            catch (Exception ex)
//            {
//                LogHelper.WriteLog("mysql", "mysqlGetDataSet " + cmdText+ ex.StackTrace);
//            }
//            finally
//            {
//                if (cmd.Connection.State == ConnectionState.Open)
//                {
//                    cmd.Connection.Close();
//                }
//                cmd.Dispose();
//            }
//            return ds;
//        }
//        /// <summary>
//        /// 用指定的数据库连接字符串执行一个命令并返回一个数据表 
//        /// </summary>
//        ///<param name="connectionString">一个有效的连接字符串</param> 
//        /// <param name="cmdType">命令类型(存储过程, 文本, 等等)</param> 
//        /// <param name="cmdText">存储过程名称或者sql命令语句</param> 
//        /// <param name="commandParameters">执行命令所用参数的集合</param> 
//        public static DataTable ExecuteTable(CommandType cmdType, string cmdText, params MySqlParameter[] cmdParameters)
//        {
//            return GetDataTable(_connStr, CommandType.Text, cmdText, false, null);
//        }
//        /// <summary>
//        /// 用指定的数据库连接字符串执行一个命令并返回一个数据表 
//        /// </summary>
//        ///<param name="connectionString">一个有效的连接字符串</param> 
//        /// <param name="cmdType">命令类型(存储过程, 文本, 等等)</param> 
//        /// <param name="cmdText">存储过程名称或者sql命令语句</param> 
//        /// <param name="commandParameters">执行命令所用参数的集合</param> 
//        public static DataTable GetDataTable(string connectionString, CommandType cmdType, string cmdText, bool isTran, params MySqlParameter[] commandParameters)
//        {
//            DataTable dt = null;
//            MySqlCommand cmd = new MySqlCommand();
//            MySqlConnection conn = new MySqlConnection(connectionString);
//            try
//            {
//                //DateTime beginDateTime = DateTime.Now;
//                PrepareCommand(cmd, conn, isTran, cmdType, cmdText, commandParameters);
//                //DateTime beginOpenDateTime = DateTime.Now;
//                //logger.LogDebug("mysqlGetDataTable打开数据库连接 " + cmdText + "耗时" + ":" + (beginOpenDateTime - beginDateTime).TotalMilliseconds);

//                MySqlDataAdapter adapter = new MySqlDataAdapter();
//                adapter.SelectCommand = cmd;
//                dt = new DataTable();

//                adapter.Fill(dt);
//                cmd.Parameters.Clear();
//                conn.Close();
//                //DateTime endDateTime = DateTime.Now;
//                //logger.LogDebug("mysqlGetDataTable " + cmdText + "耗时" + ":" + (endDateTime - beginDateTime).TotalMilliseconds);
//            }
//            catch (Exception ex)
//            {
//                LogHelper.WriteLog("mysql",  "mysqlGetDataTable " + cmdText+ ex.StackTrace);
//            }
//            finally
//            {
//                if (cmd.Connection.State == ConnectionState.Open)
//                {
//                    cmd.Connection.Close();
//                }
//                cmd.Dispose();
//            }
//            return dt;
//        }

//        ///// <summary> 
//        ///// 用指定的数据库连接字符串执行一个命令并返回一个数据集的第一列 
//        ///// </summary> 
//        ///// <remarks> 
//        /////例如: 
//        ///// Object obj = ExecuteScalar(connString, CommandType.StoredProcedure, "PublishOrders", new MySqlParameter("@prodid", 24)); 
//        ///// </remarks> 
//        /////<param name="connectionString">一个有效的连接字符串</param> 
//        ///// <param name="cmdType">命令类型(存储过程, 文本, 等等)</param> 
//        ///// <param name="cmdText">存储过程名称或者sql命令语句</param> 
//        ///// <param name="commandParameters">执行命令所用参数的集合</param> 
//        ///// <returns>用 Convert.To{Type}把类型转换为想要的 </returns> 
//        //public static object ExecuteScalar(string connectionString, CommandType cmdType, string cmdText, params MySqlParameter[] commandParameters)
//        //{
//        //    MySqlCommand cmd = new MySqlCommand();
//        //    using (MySqlConnection connection = new MySqlConnection(connectionString))
//        //    {
//        //        PrepareCommand(cmd, connection, null, cmdType, cmdText, commandParameters);
//        //        object val = cmd.ExecuteScalar();
//        //        cmd.Parameters.Clear();
//        //        return val;
//        //    }
//        //}

//        ///// <summary>
//        ///// 返回插入值ID
//        ///// </summary>
//        ///// <param name="connectionString"></param>
//        ///// <param name="cmdType"></param>
//        ///// <param name="cmdText"></param>
//        ///// <param name="commandParameters"></param>
//        ///// <returns></returns>
//        //public static object ExecuteNonExist(string connectionString, CommandType cmdType, string cmdText, params MySqlParameter[] commandParameters)
//        //{
//        //    MySqlCommand cmd = new MySqlCommand();

//        //    using (MySqlConnection connection = new MySqlConnection(connectionString))
//        //    {
//        //        PrepareCommand(cmd, connection, null, cmdType, cmdText, commandParameters);
//        //        object val = cmd.ExecuteNonQuery();

//        //        return cmd.LastInsertedId;
//        //    }
//        //}

//        ///// <summary> 
//        ///// 用指定的数据库连接执行一个命令并返回一个数据集的第一列 
//        ///// </summary> 
//        ///// <remarks> 
//        ///// 例如: 
//        ///// Object obj = ExecuteScalar(connString, CommandType.StoredProcedure, "PublishOrders", new MySqlParameter("@prodid", 24)); 
//        ///// </remarks> 
//        ///// <param name="connection">一个存在的数据库连接</param> 
//        ///// <param name="cmdType">命令类型(存储过程, 文本, 等等)</param> 
//        ///// <param name="cmdText">存储过程名称或者sql命令语句</param> 
//        ///// <param name="commandParameters">执行命令所用参数的集合</param> 
//        ///// <returns>用 Convert.To{Type}把类型转换为想要的 </returns> 
//        //public static object ExecuteScalar(MySqlConnection connection, CommandType cmdType, string cmdText, params MySqlParameter[] commandParameters)
//        //{

//        //    MySqlCommand cmd = new MySqlCommand();

//        //    PrepareCommand(cmd, connection, null, cmdType, cmdText, commandParameters);
//        //    object val = cmd.ExecuteScalar();
//        //    cmd.Parameters.Clear();
//        //    return val;
//        //}




//        /// <summary> 
//        /// 准备执行一个命令 
//        /// </summary> 
//        /// <param name="cmd">sql命令</param> 
//        /// <param name="conn">OleDb连接</param> 
//        /// <param name="trans">OleDb事务</param> 
//        /// <param name="cmdType">命令类型例如 存储过程或者文本</param> 
//        /// <param name="cmdText">命令文本,例如:Select * from Products</param> 
//        /// <param name="cmdParms">执行命令的参数</param> 
//        private static void PrepareCommand(MySqlCommand cmd, MySqlConnection conn, bool trans, CommandType cmdType, string cmdText, MySqlParameter[] cmdParms)
//        {

//            if (conn.State != ConnectionState.Open)
//                conn.Open();

//            cmd.Connection = conn;
//            cmd.CommandText = cmdText;

//            if (trans)
//                cmd.Transaction = conn.BeginTransaction(); ;

//            cmd.CommandType = cmdType;

//            if (cmdParms != null)
//            {
//                foreach (MySqlParameter parm in cmdParms)
//                    cmd.Parameters.Add(parm);
//            }
//        }


//    }
//}