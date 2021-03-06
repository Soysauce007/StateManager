﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Web;
using System.Net;
using System.IO;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;

namespace StateManager
{
    [System.Runtime.InteropServices.ComVisible(true)]
    public partial class SMHttpApiForm:Form,IState
    {
        public SMHttpApiForm()
        {
            InitializeComponent();
        }

        #region 客户端

        /// <summary>
        /// HttpRequest获取或提交 
        /// </summary>
        /// <param name="url">http://xxx,WebApi地址</param>
        /// <param name="strContent">POST时提交的数据</param>
        /// <param name="method">GET或POST</param>
        /// <param name="timeout">超时时间，毫秒</param>
        /// <returns></returns>
        public string HttpRequest(string url, string postData = "", string method = "POST", int timeout = 5000)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = method;
            request.Timeout = timeout;
            //request.ContentType = "text/html;charset=UTF-8";
            request.ContentType = "application/json;charset=UTF-8";
            //request.TransferEncoding = Encoding.UTF8;
            request.UserAgent = "Mozilla/5.0 (Windows NT 6.1; rv:19.0) Gecko/20100101 Firefox/19.0";
            //request.ContentLength = postdatabyte.Length;
            //request.AllowAutoRedirect = false;
            //request.KeepAlive = false;
            switch (method)
            {
                //GET时不需要写参数
                case "POST":
                    using (StreamWriter dataStream = new StreamWriter(request.GetRequestStream()))
                    {
                        dataStream.Write(postData);
                        dataStream.Close();
                    }
                    break;
            }

            //显示
            if (ClientViewMode)
            {
                SManager.SetText(textBoxUrl, url, true);
                SManager.SetText(textBoxClientSendData, postData, true);
                SManager.SetText(richTextBoxClientReceivedData, "", true);
            }

            HttpWebResponse response = (HttpWebResponse)request.GetResponse();

            string encoding = response.ContentEncoding;
            if (encoding == null || encoding.Length < 1) encoding = "UTF-8"; //默认编码
            Stream stream = response.GetResponseStream();
            StreamReader reader = new StreamReader(stream, Encoding.GetEncoding(encoding));
            string retString = reader.ReadToEnd();
            reader.Close();
            stream.Close();

            //显示
            if (ClientViewMode)
            {
                SManager.SetText(richTextBoxClientReceivedData, retString, true);
            }

            return retString;
        }

        public bool ClientViewMode = false;

        private void checkBoxClientViewMode_CheckedChanged(object sender, EventArgs e)
        {
            ClientViewMode = checkBoxClientViewMode.Checked;
            textBoxClientSendData.ReadOnly = ClientViewMode;
            textBoxUrl.ReadOnly = ClientViewMode;
            button1.Enabled = !ClientViewMode;
        }

        private void button1_Click(object sender, EventArgs e)
        {
            try
            {
                richTextBoxClientReceivedData.Text = HttpRequest(textBoxUrl.Text, textBoxClientSendData.Text, comboBox1.Text);
            }
            catch (Exception E)
            {
                MessageBox.Show(E.Message);
            }
        }

        
        #endregion


        #region 服务端

        public int ServerPort = 666;
        public HttpListener listener;
        //业务回调事件
        public delegate string deleHandleRequests(HttpListenerRequest request, string postData);
        public deleHandleRequests ServerOnHandleRequests;
        public bool ServerActive=false;
        public void ServerStart()
        {
            if (listener == null)
                listener = new HttpListener();
            listener.Stop();
            listener.AuthenticationSchemes = AuthenticationSchemes.Anonymous;//指定身份验证 Anonymous匿名访问
            listener.Prefixes.Add(string.Format("http://+:{0}/", ServerPort.ToString()));
            listener.Start();
            SManager.SetText(tabPage2, string.Format("服务端（已打开端口：{0}）", ServerPort), true);
            SManager.SetEnabled(buttonServerStart, false, true);
            SManager.SetEnabled(buttonServerStop, true, true);
            ServerActive = true;
            listener.BeginGetContext(new AsyncCallback(GetContext), listener);  //开始异步接收request请求
        }

        //public static void CmdRun(string cmd,string param)
        //{
        //    System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo(cmd, param);
        //    psi.UseShellExecute = true;
        //    psi.WorkingDirectory = Environment.CurrentDirectory;
        //    psi.Verb = "runas";
        //    psi.CreateNoWindow = true;
        //    psi.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
        //    psi.UseShellExecute = false;
        //    System.Diagnostics.Process.Start(psi).WaitForExit();
        //}
        void GetContext(IAsyncResult ar)
        {
            HttpListener listener = ar.AsyncState as HttpListener;
            HttpListenerContext context;
            try
            {
                context = listener.EndGetContext(ar);  //接收到的请求context（一个环境封装体）
            }
            catch
            {//listener.Stop()时不继续
                return;
            }

            listener.BeginGetContext(new AsyncCallback(GetContext), listener);  //开始 第二次 异步接收request请求

            HttpListenerRequest request = context.Request;  //接收的request数据
            HttpListenerResponse response = context.Response;  //用来向客户端发送回复

            //response.ContentType = "text/html; charset=UTF-8";
            response.ContentType = "application/json";
            response.ContentEncoding = Encoding.UTF8;
            response.StatusCode = 200;//设置返回给客服端http状态代码
            response.AppendHeader("Access-Control-Allow-Origin", "*");
            string postData = string.Empty;
            using (StreamReader reader = new StreamReader(request.InputStream, request.ContentEncoding))
            {
                postData = reader.ReadToEnd();
            }

            string rString = "";

            //显示
            SManager.SetText(textBoxServerRequest, string.Format("URL: {0}\r\n{1}ContentType: \r\n\r\n{2}", request.Url, request.ContentType, postData), true);

            if (ServerForceResponse)
            {
                rString = DeFaultResponseString;//（强制内容时由人工修改）窗体内容改变事件中修改的DeFaultResponseString
            }
            else
            {
                //显示
                SManager.SetText(textBoxServerResponse, "", true);

                if (ServerOnHandleRequests != null)
                {
                    rString = ServerOnHandleRequests(request, postData);
                }
                else
                {
                    rString = NotSetResponseString;  //表示未绑定处理代码
                }

                //显示
                SManager.SetText(textBoxServerResponse, rString, true);
            }


            using (Stream output = response.OutputStream)  //发送回复
            {
                byte[] buffer = Encoding.UTF8.GetBytes(rString);
                try
                {
                    output.Write(buffer, 0, buffer.Length);
                    output.Close();
                }
                catch { }
            }
            response.Close();
        }

        public void ServerStop()
        {
            if (listener != null)
                listener.Stop();
            SManager.SetText(tabPage2, string.Format("服务端", ServerPort), true);
            SManager.SetEnabled(buttonServerStart, true, true);
            SManager.SetEnabled(buttonServerStop, false, true);
            ServerActive = false;
        }

        public bool ServerForceResponse = false;

        private void checkBoxServerForceResponce_CheckedChanged(object sender, EventArgs e)
        {
            ServerForceResponse = checkBoxServerForceResponce.Checked;
        }

        private void buttonServerStart_Click(object sender, EventArgs e)
        {
            ServerPort = Convert.ToInt32(textBoxServerPort.Text);
            try
            {
                ServerStart();
            }
            catch (Exception E)
            {
                if (E.Message.Contains("拒绝访问"))
                {
                    MessageBox.Show("Http服务启动失败，请尝试使用管理员权限运行！");//或执行以下命令来添加系统对端口的权限
                    //CmdRun("netsh", string.Format("http delete urlacl url=http://+:{0}/", textBox3.Text));
                    //CmdRun("netsh", string.Format("http add urlacl url=http://+:{0}/  user=Everyone", textBox3.Text));
                    //MessageBox.Show("完成初始化，将自动开始..");
                    //button2_Click(null, null);
                }
                else
                    MessageBox.Show(E.Message);
            }
        }

        private void buttonServerStop_Click(object sender, EventArgs e)
        {
            ServerStop();
        }

        string NotSetResponseString = "{errorcode:40000,errorstring:\"测试专用\",result:\"\"}";
        string DeFaultResponseString;
        private void textBoxServerResponse_TextChanged(object sender, EventArgs e)
        {
            DeFaultResponseString = textBoxServerResponse.Text;
        }

        #endregion


        //状态机接口

        object IState.Form
        {
            get { return this; }
        }

        void IState.StateFInit(SObject so)
        {
            ServerStop();
        }

        void IState.StateHandle(ref SObject so)
        {

        }

        void IState.StateInit(SObject so)
        {

        }



    }
}
