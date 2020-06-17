using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using chatLib;

namespace Client
{
    public partial class Client : Form
    {

        public TextBox tbMesajGonder;
        public Button btnDosyaGonder;
        public Button btnGirisYap;

        public String kullaniciAdi;

        private bool connected = false;
        private Thread client = null;

        public delegate void msgDelegate(chatLib.Message msg);

        public event msgDelegate msgEvent = delegate { };


        private struct MyClient
        {
            public TcpClient client;
            public NetworkStream stream;
            public byte[] buffer;
            public StringBuilder data;
            public EventWaitHandle handle;
        };
        MyClient obj;
        public Task send = null;
        private bool exit = false;

        public Client()
        {
            InitializeComponent();

            tbMesajGonder = sendTextBox;
            btnDosyaGonder = button1;
            btnGirisYap = btnLogin;
        }

        private void LogWrite(string msg = null)
        {
            if (!exit)
            {
                logTextBox.Invoke((MethodInvoker)delegate
                {
                    if (msg == null)
                    {
                        logTextBox.Clear();
                        
                    }
                    else
                    {
                        if (logTextBox.Text.Length > 0)
                        {
                            logTextBox.AppendText(Environment.NewLine);
                        }
                        logTextBox.AppendText(DateTime.Now.ToString("HH:mm") + " " +  msg);
                    }
                });
            }
        }

        private void Connected(bool status)
        {
            if (!exit)
            {
                connected = status;
                connectButton.Invoke((MethodInvoker)delegate
                {
                    if (status)
                    {
                        btnLogin.Invoke((MethodInvoker)delegate
                        {
                            btnLogin.Enabled = true;
                        });
                        connectButton.Text = "Disconnect";
                        LogWrite("[/ Client connected /]");
                    }
                    else
                    {
                        btnLogin.Invoke((MethodInvoker)delegate
                        {
                            btnLogin.Enabled = false;
                        });
                        sendTextBox.Enabled = false;
                        button1.Enabled = false;
                        connectButton.Text = "Connect";
                        LogWrite("[/ Client disconnected /]");
                    }
                });
            }
        }

        private void Read(IAsyncResult result)
        {
            int bytes = 0;
            if (obj.client.Connected)
            {
                try
                {
                    bytes = obj.stream.EndRead(result);
                }
                catch (IOException e) { LogWrite(String.Format("[/ {0} /]", e.Message)); }
                catch (ObjectDisposedException e) { LogWrite(String.Format("[/ {0} /]", e.Message)); }
            }
            if (bytes > 0)
            {
                // obj.data.AppendFormat("{0}", Encoding.UTF8.GetString(obj.buffer, 0, bytes));



                bool dataAvailable = false;
                try
                {
                    dataAvailable = obj.stream.DataAvailable;
                }
                catch (IOException e) { LogWrite(String.Format("[/ {0} /]", e.Message)); }
                catch (ObjectDisposedException e) { LogWrite(String.Format("[/ {0} /]", e.Message)); }
                if (dataAvailable)
                {
                    try
                    {
                        obj.stream.BeginRead(obj.buffer, 0, obj.buffer.Length, new AsyncCallback(Read), null);
                    }
                    catch (IOException e)
                    {
                        LogWrite(String.Format("[/ {0} /]", e.Message));
                        obj.handle.Set();
                    }
                    catch (ObjectDisposedException e)
                    {
                        LogWrite(String.Format("[/ {0} /]", e.Message));
                        obj.handle.Set();
                    }
                }
                else
                {
                    IFormatter formatter = new BinaryFormatter();
                    MemoryStream ms = new MemoryStream(obj.buffer);
                    chatLib.Message msg = (chatLib.Message)formatter.Deserialize(ms);

                    switch (msg.Head)
                    {
                        case chatLib.Message.Header.POST:
                            LogWrite(string.Format("{0} - {1}", msg.MessageList[0], msg.MessageList[1]));
                            break;
                        case chatLib.Message.Header.FILE:
                            LogWrite(string.Format("{0} - {1} Dosya Gönderdi.", msg.MessageList[0], msg.MessageList[1]));
                            byte[] buffer = System.Convert.FromBase64String(msg.MessageList[2]);
                            FileStream fs = new FileStream(AppDomain.CurrentDomain.BaseDirectory + "download\\" + msg.MessageList[1], FileMode.CreateNew);
                            fs.Write(buffer, 0, buffer.Length);
                            fs.Close();
                            break;

                        case chatLib.Message.Header.REGISTER:
                            msgEvent?.Invoke(msg);
                            break;
                        case chatLib.Message.Header.JOIN:
                            msgEvent?.Invoke(msg);
                            break;
                    }
                    //obj.data.Clear();
                    obj.handle.Set();
                }
            }
            else
            {
                obj.client.Close();
                obj.handle.Set();
            }
        }

        private void Connection(IPAddress localaddr, Int32 port)
        {
            try
            {
                obj = new MyClient();
                obj.client = new TcpClient();
                obj.client.Connect(localaddr, port);
                obj.stream = obj.client.GetStream();
                obj.buffer = new byte[obj.client.ReceiveBufferSize];
                obj.data = new StringBuilder();
                obj.handle = new EventWaitHandle(false, EventResetMode.AutoReset);
                Connected(true);

                if (obj.stream.CanRead && obj.stream.CanWrite)
                {
                    while (obj.client.Connected)
                    {
                        try
                        {
                            obj.stream.BeginRead(obj.buffer, 0, obj.buffer.Length, new AsyncCallback(Read), null);
                            obj.handle.WaitOne();
                        }
                        catch (IOException e) { LogWrite(String.Format("[/ {0} /]", e.Message)); }
                        catch (ObjectDisposedException e) { LogWrite(String.Format("[/ {0} /]", e.Message)); }
                    }
                }
                else
                {
                    LogWrite("[/ Stream cannot read/write /]");
                }
                obj.client.Close();
                Connected(false);
            }
            catch (SocketException e) { LogWrite(String.Format("[/ {0} /]", e.Message)); }
        }

        private void ConnectButton_Click(object sender, EventArgs e)
        {
            if (connected)
            {
                obj.client.Close();
            }
            else
            {
                if (client == null || !client.IsAlive)
                {
                    bool localaddrResult = IPAddress.TryParse(localaddrMaskedTextBox.Text, out IPAddress localaddr);
                    if (!localaddrResult)
                    {
                        LogWrite("[/ Address is not valid /]");
                    }
                    bool portResult = Int32.TryParse(portTextBox.Text, out Int32 port);
                    if (!portResult)
                    {
                        LogWrite("[/ Port is not valid /]");
                    }
                    else if (port < 0 || port > 65535)
                    {
                        portResult = false;
                        LogWrite("[/ Port is out of range /]");
                    }
                    if (localaddrResult && portResult)
                    {
                        client = new Thread(() => Connection(localaddr, port))
                        {
                            IsBackground = true
                        };
                        client.Start();
                        //Bağlantı var.
                    }
                }
            }
        }

        private void Write(IAsyncResult result)
        {
            if (obj.client.Connected)
            {
                try
                {
                    obj.stream.EndWrite(result);
                }
                catch (IOException e) { LogWrite(String.Format("[/ {0} /]", e.Message)); }
                catch (ObjectDisposedException e) { LogWrite(String.Format("[/ {0} /]", e.Message)); }
            }
        }

        public void Send(chatLib.Message msg)
        {
            try
            {
                IFormatter formatter = new BinaryFormatter();
                MemoryStream memStream = new MemoryStream();
                formatter.Serialize(memStream, msg);
                byte[] buffer = memStream.GetBuffer();
                obj.stream.BeginWrite(buffer, 0, buffer.Length, new AsyncCallback(Write), null);
            }
            catch (IOException e) { LogWrite(String.Format("[/ {0} /]", e.Message)); }
            catch (ObjectDisposedException e) { LogWrite(String.Format("[/ {0} /]", e.Message)); }
        }

        private void SendTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                if (sendTextBox.Text != "")
                {
                    String msg = sendTextBox.Text;
                    sendTextBox.Clear();
                    LogWrite("<- You -> " + msg);
                    if (connected)
                    {
                        chatLib.Message message = new chatLib.Message(chatLib.Message.Header.POST);
                        message.addData(kullaniciAdi);
                        message.addData(msg);

                        if (send == null || send.IsCompleted)
                        {
                            
                            send = Task.Factory.StartNew(() => Send(message));
                        }
                        else
                        {
                            send.ContinueWith(antecendent => Send(message));
                        }
                    }
                }
            }
        }

        private void Client_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (connected)
            {
                exit = true;
                obj.client.Close();
            }
        }

        private void ClearButton_Click(object sender, EventArgs e)
        {
            LogWrite();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            if (connected)
            {
                openFileDialog1.ShowHelp = true;
                if (openFileDialog1.ShowDialog() == DialogResult.OK)
                {
                    chatLib.Message message = new chatLib.Message(chatLib.Message.Header.FILE);

                    byte[] bytes = File.ReadAllBytes(openFileDialog1.FileName);
                    string contents = System.Convert.ToBase64String(bytes);
                    message.addData(kullaniciAdi);
                    message.addData(openFileDialog1.SafeFileName);
                    message.addData(contents);

                    if (send == null || send.IsCompleted)
                    {

                        send = Task.Factory.StartNew(() => Send(message));
                    }
                    else
                    {
                        send.ContinueWith(antecendent => Send(message));
                    }

                    LogWrite(string.Format("{0} - {1} Dosyası Gönderildi.", kullaniciAdi, openFileDialog1.SafeFileName));

                }
            }
            else
            {
                MessageBox.Show("Bağlantı Yok");
            }
        }

        private void btnLogin_Click(object sender, EventArgs e)
        {
            login lgn = new login();
            lgn.client = this;
            msgEvent += new Client.msgDelegate(lgn.MessageHandler);
            lgn.ShowDialog();
        }

        private void Client_Load(object sender, EventArgs e)
        {

        }

        private void linkLabel1_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Process.Start("explorer.exe", AppDomain.CurrentDomain.BaseDirectory + @"download");
        }
    }
}
