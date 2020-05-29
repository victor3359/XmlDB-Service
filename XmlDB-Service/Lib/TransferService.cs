using System;
using System.IO;
using System.Xml;
using System.Net;
using System.Timers;
using System.Diagnostics;
using System.Collections.Generic;
using MySql.Data.MySqlClient;
using XmlDB_Service.Lib;

namespace XmlDB_Service
{
    class TransferService
    {
        private readonly Timer _timer, _chkTimer;
        private static string InIFileStr = @"XmlDBConfig.ini";
        InIManager _iniManager = new InIManager(InIFileStr, 1024);
        private static int updRate;
        private static bool initTable = true;
        private static string DbHost, User, Pwd, DbName, DbTable, Field, DaysOfStorage;
        private static DateTime localDate;
        public static DateTime LocalDate { get => localDate; set => localDate = value; }

        public TransferService()
        {
            readConfiguration();
            _timer = new Timer(updRate) { AutoReset = true };
            _chkTimer = new Timer(86400000) { AutoReset = true }; //86400000ms a day
            _timer.Elapsed += TransferProcess;
            _chkTimer.Elapsed += mariaDB_CheckEachDayAsync;
        }

        private void TransferProcess(object sender, ElapsedEventArgs e)
        {
            List<string> firePower = new List<string>();
            List<string> solarPower = new List<string>();
            List<string> windPower = new List<string>();
            string fileURL = @"http://data.taipower.com.tw/opendata/apply/file/d004006/002.xml";
            string solarURL = @"http://data.taipower.com.tw/opendata/apply/file/d004006/003.xml";
            string windURL = @"http://data.taipower.com.tw/opendata/apply/file/d004006/004.xml";

            XmlDocument xmlDoc_fire = new XmlDocument();
            XmlDocument xmlDoc_solar = new XmlDocument();
            XmlDocument xmlDoc_wind = new XmlDocument();
            double totalFire = 0, totalSolar = 0, totalWind = 0;
            XmlNode xn;
            string xmlStr;

            Console.WriteLine(@"Loading Xml Infomations...");
            //FirePower
            using (var wc = new WebClient())
            {
                xmlStr = wc.DownloadString(fileURL);
            }
            xmlDoc_fire.LoadXml(xmlStr);
            xn = xmlDoc_fire.SelectSingleNode(@"/DATAPACKET/ROWDATA/ROW[@EpName='塔山']");
            foreach (XmlAttribute attr in xn.Attributes)
            {
                if (attr.Name.Contains(@"Value"))
                {
                    if (attr.Value != @"")
                        firePower.Add((Convert.ToDouble(attr.Value) * 1000).ToString());
                }
            }
            xn = xmlDoc_fire.SelectSingleNode(@"/DATAPACKET/ROWDATA/ROW[@EpName='夏興']");
            foreach (XmlAttribute attr in xn.Attributes)
            {
                if (attr.Name.Contains(@"Value"))
                {
                    if (attr.Value != @"")
                        firePower.Add((Convert.ToDouble(attr.Value) * 1000).ToString());
                }
            }
            
            //SolarPower
            using (var wc = new WebClient())
            {
                xmlStr = wc.DownloadString(solarURL);
            }
            xmlDoc_solar.LoadXml(xmlStr);
            xn = xmlDoc_solar.SelectSingleNode(@"/NewDataSet/table[電廠='金門金沙']");
            foreach (XmlNode n in xn)
            {
                if (n.Name.Equals(@"日照計"))
                {
                    solarPower.Add(n.InnerText);
                    solarPower.Add((Convert.ToDouble(n.InnerText) * 6.977418).ToString());
                }
            }

            //WindPower
            using (var wc = new WebClient())
            {
                xmlStr = wc.DownloadString(windURL);
            }
            xmlDoc_wind.LoadXml(xmlStr);
            xn = xmlDoc_wind.SelectSingleNode(@"/NewDataSet/table[EpCode='金門金沙']");
            foreach (XmlNode n in xn)
            {
                if (n.Name.Contains(@"Value"))
                {
                    windPower.Add(n.InnerText);
                }
            }

            if (initTable)
            {
                Console.WriteLine(@"Clear realtime Table...");
                var connString = $"Server={DbHost};User Id={User};Password={Pwd};Database={DbName}";
                //Console.WriteLine(connString);
                try
                {
                    using (var conn = new MySqlConnection(connString))
                    {
                        conn.Open();
                        using (var cmd = new MySqlCommand())
                        {
                            cmd.Connection = conn;
                            cmd.CommandText = $"TRUNCATE TABLE realtime";
                            cmd.ExecuteNonQuery();
                        }
                    }
                }
                catch (MySqlException ex)
                {
                    string[] Error = new string[] { $"{LocalDate}\t\t{ex.Message}" };
                    File.AppendAllLines(@"./SqlConnectionError.log", Error);
                    Console.WriteLine(ex.Message);
                }
            }

            Console.WriteLine(@"Updating Data to Database...");
            for (int i = 0; i < firePower.Count; i++)
            {
                string valueString;
                if (i <= 9)
                {
                    valueString = $"'火力','Value{i + 1}','塔山 {i + 1}','{firePower[i]}'";
                }
                else
                {
                    valueString = $"'火力','Value{i + 1}','夏興 {i + 1}','{firePower[i]}'";
                }
                saveToSql(valueString, $"Value{i + 1}", firePower[i], @"火力");
                totalFire += Convert.ToDouble(firePower[i]);
            }
            for(int i = 0; i < solarPower.Count; i++)
            {
                string valueString;
                switch (i)
                {
                    case 0:
                        valueString = $"'太陽能','日照計','金門金沙','{solarPower[i]}'";
                        saveToSql(valueString, @"日照計", solarPower[i], @"太陽能");
                        break;
                    case 1:
                        valueString = $"'太陽能','發電量','金門金沙','{solarPower[i]}'";
                        saveToSql(valueString, @"發電量", solarPower[i], @"太陽能");
                        totalSolar += Convert.ToDouble(solarPower[i]);
                        break;
                }
            }
            for(int i = 0; i < windPower.Count; i++)
            {
                string valueString = $"'風力','Value{i + 1}','金沙風力 {i + 1}','{windPower[i]}'";
                saveToSql(valueString, $"Value{i + 1}", windPower[i], @"風力");
                totalWind += Convert.ToDouble(windPower[i]);
            }
            if (initTable)
            {
                initTable = false;
            }

            try
            {
                var connString = $"Server={DbHost};User Id={User};Password={Pwd};Database={DbName}";
                using (var conn = new MySqlConnection(connString))
                {
                    conn.Open();
                    using (var cmd = new MySqlCommand())
                    {
                        cmd.Connection = conn;
                        cmd.CommandText = $"INSERT INTO totalfire(power) VALUES ('{totalFire}')";
                        cmd.ExecuteNonQuery();
                        cmd.CommandText = $"INSERT INTO totalsolar(power) VALUES ('{totalSolar}')";
                        cmd.ExecuteNonQuery();
                        cmd.CommandText = $"INSERT INTO totalwind(power) VALUES ('{totalWind}')";
                        cmd.ExecuteNonQuery();
                        cmd.CommandText = $"INSERT INTO totalpower(power) VALUES ('{totalFire + totalSolar + totalWind}')";
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (MySqlException ex)
            {
                string[] Error = new string[] { $"{LocalDate}\t\t{ex.Message}" };
                File.AppendAllLines(@"./SqlConnectionError.log", Error);
                Console.WriteLine(ex.Message);
            }
            Console.WriteLine(@"Work Done.");
        }
        private void saveToSql(string valueStr, string ItemId, string value, string type)
        {
            var connString = $"Server={DbHost};User Id={User};Password={Pwd};Database={DbName}";
            try
            {
                using (var conn = new MySqlConnection(connString))
                {
                    conn.Open();
                    using (var cmd = new MySqlCommand())
                    {
                        cmd.Connection = conn;
                        if (initTable)
                        {
                            cmd.CommandText = $"INSERT INTO realtime(Type,ItemId,realN,Value) VALUES ({valueStr})";
                            cmd.ExecuteNonQuery();
                            cmd.CommandText = $"INSERT INTO historical(Type,ItemId,realN,Value) VALUES ({valueStr})";
                            cmd.ExecuteNonQuery();
                        }
                        else
                        {
                            cmd.CommandText = $"INSERT INTO historical(Type,ItemId,realN,Value) VALUES ({valueStr})";
                            cmd.ExecuteNonQuery();
                            cmd.CommandText = $"UPDATE realtime SET Value='{value}' WHERE ItemId='{ItemId}' AND Type='{type}'";
                            cmd.ExecuteNonQuery();
                        }
                    }
                }
            }
            catch (MySqlException ex)
            {
                string[] Error = new string[] { $"{LocalDate}\t\t{ex.Message}" };
                File.AppendAllLines(@"./SqlConnectionError.log", Error);
                Console.WriteLine(ex.Message);
            }
        }
        public void Start()
        {
            _timer.Start();
            _chkTimer.Start();
        }
        public void Stop()
        {
            _timer.Stop();
            _chkTimer.Stop();
        }
        private void readConfiguration()
        {
            updRate = Convert.ToInt32(_iniManager.IniReadValue(@"MariaSql", @"UpdateRate"));
            DbHost = _iniManager.IniReadValue(@"MariaSql", @"DbHost");
            User = _iniManager.IniReadValue(@"MariaSql", @"User");
            Pwd = _iniManager.IniReadValue(@"MariaSql", @"Pwd");
            DbName = _iniManager.IniReadValue(@"MariaSql", @"DbName");
            DbTable = _iniManager.IniReadValue(@"MariaSql", @"DbTable");
            Field = _iniManager.IniReadValue(@"MariaSql", @"Field");
            DaysOfStorage = _iniManager.IniReadValue(@"MariaSql", @"DaysOfStorage");
        }

        private async void mariaDB_CheckEachDayAsync(object sender, ElapsedEventArgs e)
        {
            var connString = $"Server={DbHost};User Id={User};Password={Pwd};Database={DbName}";

            try
            {
                using (var conn = new MySqlConnection(connString))
                {
                    await conn.OpenAsync();
                    // Insert some data
                    using (var cmd = new MySqlCommand())
                    {
                        cmd.Connection = conn;
                        cmd.CommandText = $"DELETE FROM {DbTable} WHERE DATE({Field}) = DATE_ADD(CURDATE(), INTERVAL -{DaysOfStorage} DAY);";
                        await cmd.ExecuteNonQueryAsync();
                    }
                }
            }
            catch (MySqlException ex)
            {
                string[] Error = new string[] { $"{LocalDate}\t\t{ex.Message}" };
                File.AppendAllLines(@"./SqlConnectionError.log", Error);
                Console.WriteLine(ex.Message);
            }
        }
    }
}
