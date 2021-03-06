﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;
using Newtonsoft.Json.Utilities;
using Newtonsoft.Json;
using System.IO;
using System.Globalization;
using System.Text.RegularExpressions;

namespace MiningMonitorClientW
{
    class WorkerUpdate
    {
        public string wun { get; set; }
        public int a { get; set; }
        public int r { get; set; }
        public int he { get; set; }
        public double[] gs { get; set; }
        public string p { get; set; }
        public int po { get; set; }

        public void update(string user_worker, bool logging)
        {    
                 //query the miner for summary and gpucount information
                 String SummaryQuery = QueryMiner("summary", logging); 
                 String gpuNum = FindKey(QueryMiner("gpucount", logging), "Count");
                 int numgpus = Convert.ToInt32(gpuNum);
                 //Array of strings to hold each gpu query 
                 String[] gpuQueries = new String[numgpus];
                  //add the GPU queries into the array
                 for (int i = 0; i < numgpus; i++)
                    gpuQueries[i] = QueryMiner("gpu|" + i, logging);

                  //now add information specific to each gpu to a list
                  List<double> gpuList = new List<double>();
                  CultureInfo US = new CultureInfo("en-US");
                  for (int i = 0; i < gpuQueries.Length; i++)
                  {
                      gpuList.Add(Convert.ToDouble(FindKey(gpuQueries[i], "Temperature"), US));
                      gpuList.Add(Convert.ToDouble(FindKey(gpuQueries[i], "MHS 5s"), US));
                  }
                  //Do all the pool work
                  String  PoolQuery = QueryMiner("pools", logging);
                  String CurPool = FindKey(PoolQuery, "URL");
                  String PoolOnline = FindKey(PoolQuery, "Status");
                  int pa = PoolOnline == "Alive"? 1 : 0;
                  Regex regex =  new Regex("//(\\w+).com");
                  Match match = regex.Match(CurPool);
                  CurPool = match.Success? match.Groups[1].Value : CurPool;
                  //set all the values that we have gotten from the queries
                  this.wun = user_worker;
                  this.a = Convert.ToInt32(FindKey(SummaryQuery, "Accepted"), US);
                  this.r = Convert.ToInt32(FindKey(SummaryQuery, "Rejected"), US);
                  this.he = Convert.ToInt32(FindKey(SummaryQuery, "Hardware Errors"), US);
                  this.gs = gpuList.ToArray();
                  this.p = CurPool;
                  this.po = pa;
                  
                  //create JSON from the workerUpdate object
                  string JSON = JsonConvert.SerializeObject(this);
                  // for testing string JSON = "{\"wun\":\"1:worker1\",\"h\":\"1.91\",\"a\":\"5995\",\"r\":\"153\",\"he\":\"0\",\"gpus\":[\"72.00\",\"0.64\",\"73.00\",\"0.64\",\"74.00\",\"0.63\"]}";
                  //send to website
                  HttpPutRequest(JSON, logging);
        }
        static string QueryMiner(string command, bool logging)
        {
            byte[] bytes = new byte[1024];
            try
            {
                //code for gettting current machines IP Use this code if client is running on the miner
                IPAddress ipAddr = IPAddress.Parse("127.0.0.1");            
                IPEndPoint ipEndPoint = new IPEndPoint(ipAddr, 4028);

                Socket sender = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                sender.Connect(ipEndPoint);

                string SummaryMessage = command;
                byte[] msg = Encoding.ASCII.GetBytes(SummaryMessage);

                sender.Send(msg);
                byte[] buffer = new byte[1024];
                int lengthOfReturnedBuffer = sender.Receive(buffer);
                char[] chars = new char[lengthOfReturnedBuffer];

                Decoder d = System.Text.Encoding.UTF8.GetDecoder();
                int charLen = d.GetChars(buffer, 0, lengthOfReturnedBuffer, chars, 0);
                String SummaryJson = new String(chars);
                sender.Shutdown(SocketShutdown.Both);
                sender.Close();
                if(logging)
                    Logger("Query " + command + ": "  + SummaryJson + System.Environment.NewLine);
                return SummaryJson;
            }
            catch (Exception ex)
            {
                if (logging)
                {
                    Logger("Exception: " + ex.ToString());
                }
                return ex.ToString();
            }
        }

        private static void Logger(String lines)
        {        
            // Write the string to a file.append mode is enabled so that the log
            // lines get appended to  test.txt than wiping content and writing the log
            string LogFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClientLogs.txt");
            System.IO.StreamWriter file = new System.IO.StreamWriter(LogFilePath, true);
            file.WriteLine(lines);
            file.Close();
        }
        static void HttpPutRequest(string Json, bool logging)
        {
            try
            {
                HttpWebRequest Request = (HttpWebRequest)WebRequest.Create("https://cryptocanary.herokuapp.com/workers/update");
                Request.ContentType = "application/json";
                Request.Method = "PUT";
                Request.Timeout = 120000; //not sure if correct
                byte[] bytes = Encoding.UTF8.GetBytes(Json);
                Request.ContentLength = bytes.Length;

                Stream dataStream = Request.GetRequestStream();
                dataStream.Write(bytes, 0, bytes.Length);
                dataStream.Close();
                HttpWebResponse response = (HttpWebResponse)Request.GetResponse();
                Stream RdataStream = response.GetResponseStream();
                StreamReader reader = new StreamReader(RdataStream);
                if (logging)
                {
                    Logger("Sending JSON: " + Json);
                    Logger("To URL: " + Request.Address);
                    Logger("Status: " + ((HttpWebResponse)response).StatusDescription);
                    Logger("Response: " + reader.ReadToEnd());
                }
                reader.Close();
                RdataStream.Close();
                response.Close();
            }
            catch (WebException we)
            {
                if (logging)
                {
                    Logger("Web Expection Catch: " + we.ToString());
                    WebExceptionStatus status = we.Status;
                    if (status == WebExceptionStatus.ProtocolError)
                    {
                        HttpWebResponse http = (HttpWebResponse)we.Response;
                        Logger("The Server returned protocal Error: " + (int)http.StatusCode + " - " + http.StatusCode);
                    }
                }
            }
            catch (Exception ex)
            {
                if (logging)
                {
                    Logger("Expection: " + ex.ToString());
                }
            }
        }
        //Function to parse the string returns from the miner modified slightly from cgminer java api example
        static string FindKey(String result, string key)
        {
            String value;
            String name;
            String[] sections = result.Split('|');

            for (int i = 0; i < sections.Length; i++)
            {
                if (sections[i].Trim().Length > 0)
                {
                    String[] data = sections[i].Split(',');

                    for (int j = 0; j < data.Length; j++)
                    {
                        String[] nameval = data[j].Split('=');
                        if (j == 0)
                        {
                            if (nameval.Length > 1 && Char.IsDigit(nameval[1][0]))
                                name = nameval[0] + nameval[1];
                            else
                                name = nameval[0];
                        }
                        if (nameval.Length > 1)
                        {
                            name = nameval[0];
                            value = nameval[1];
                        }
                        else
                        {
                            name = "" + j;
                            value = nameval[0];
                        }
                        if (name == key)
                            return value;
                    }
                }
            }
            return "not found";
        }
    }
}
