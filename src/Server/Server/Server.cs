using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using chatLib;

namespace Server
{
    public partial class Server : Form
    {
        private bool active = false;
        private Thread listener = null;
        private long id = 0;
        private struct MyClient
        {
            public long id;
            public TcpClient client;
            public NetworkStream stream;
            public byte[] buffer;
            public StringBuilder data;
            public EventWaitHandle handle;
        };
        private ConcurrentDictionary<long, MyClient> list = new ConcurrentDictionary<long, MyClient>();
        private Task send = null;
        private Thread disconnect = null;
        private bool exit = false;

        private Hashtable users = new Hashtable();

        public Server()
        {
            InitializeComponent();
            
            if(File.Exists(AppDomain.CurrentDomain.BaseDirectory + "users.txt"))
            {
                string u = File.ReadAllText(AppDomain.CurrentDomain.BaseDirectory + "users.txt");

                string[] a = u.Split(';');

                for (int i = 0; i < a.Length; i++)
                {
                    string[] u1 = a[i].Split('|');
                    users.Add(u1[0], u1[1]);
                }
            }
        }

        public string MD5Sifrele(string metin)
        {
            // MD5CryptoServiceProvider nesnenin yeni bir instance'sını oluşturalım.
            MD5CryptoServiceProvider md5 = new MD5CryptoServiceProvider();

            //Girilen veriyi bir byte dizisine dönüştürelim ve hash hesaplamasını yapalım.
            byte[] btr = Encoding.UTF8.GetBytes(metin);
            btr = md5.ComputeHash(btr);

            //byte'ları biriktirmek için yeni bir StringBuilder ve string oluşturalım.
            StringBuilder sb = new StringBuilder();


            //hash yapılmış her bir byte'ı dizi içinden alalım ve her birini hexadecimal string olarak formatlayalım.
            foreach (byte ba in btr)
            {
                sb.Append(ba.ToString("x2").ToLower());
            }

            //hexadecimal(onaltılık) stringi geri döndürelim.
            return sb.ToString();
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
                        logTextBox.AppendText(DateTime.Now.ToString("HH:mm") + " " + msg);
                    }
                });
            }
        }

        private void Active(bool status)
        {
            if (!exit)
            {
                active = status;
                startButton.Invoke((MethodInvoker)delegate
                {
                    if (status)
                    {
                        startButton.Text = "Stop";
                        LogWrite("[/ Server started /]");
                    }
                    else
                    {
                        startButton.Text = "Start";
                        LogWrite("[/ Server stopped /]");
                    }
                });
            }
        }

        private void Read(IAsyncResult result)
        {
            MyClient obj = (MyClient)result.AsyncState;
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
                        obj.stream.BeginRead(obj.buffer, 0, obj.buffer.Length, new AsyncCallback(Read), obj);
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
                    chatLib.Message msg = (chatLib.Message) formatter.Deserialize(ms);

                    switch (msg.Head)
                    {
                        case chatLib.Message.Header.POST:
                            LogWrite(String.Format("{0} -> {1} ", msg.MessageList[0],msg.MessageList[1]));
                            break;
                        case chatLib.Message.Header.FILE:
                            LogWrite(String.Format("{0} -> {1} Adında Dosya Gönderdi ",  msg.MessageList[0],msg.MessageList[1]));
                            break;
                        case chatLib.Message.Header.REGISTER:
                            LogWrite(String.Format("{0} -> Kayıt Oldu ", msg.MessageList[0]));
                            break;
                        case chatLib.Message.Header.JOIN:
                            LogWrite(String.Format("{0} -> Giriş Yaptı ", msg.MessageList[0]));

                            break;
                    }

                    
                    //String msg = String.Format("<- Client {0} -> " + obj.data, obj.id);
                    //LogWrite(msg);
                    if (send == null || send.IsCompleted)
                    {
                        switch (msg.Head)
                        {
                            case chatLib.Message.Header.REGISTER:
                                chatLib.Message message = new chatLib.Message(chatLib.Message.Header.REGISTER);
                                if (users.ContainsKey(msg.MessageList[0]))
                                {
                                    
                                    message.addData("error");
                                }
                                else
                                {
                                    users.Add(msg.MessageList[0], MD5Sifrele(msg.MessageList[1]));
                                    message.addData("success");

                                    string text = "";
                                    foreach (DictionaryEntry item in users)
                                    {
                                        text += item.Key.ToString() + "|" + item.Value.ToString() + ";";

                                    }

                                    text = text.Trim(';');

                                    File.WriteAllText(AppDomain.CurrentDomain.BaseDirectory + "users.txt", text);

                                }
                                
                                send = Task.Factory.StartNew(() => SendOne(message, obj.id));
                                break;
                            case chatLib.Message.Header.JOIN:
                                chatLib.Message message1 = new chatLib.Message(chatLib.Message.Header.JOIN);
                                if (users.ContainsKey(msg.MessageList[0]))
                                {
                                    if (users[msg.MessageList[0]].Equals(MD5Sifrele(msg.MessageList[1])))
                                    {
                                        message1.addData("success");
                                        message1.addData(msg.MessageList[0]);
                                    }
                                    message1.addData("error");
                                }
                                else
                                {
                                    message1.addData("error");
                                }

                                send = Task.Factory.StartNew(() => SendOne(message1, obj.id));
                                break;
                            case chatLib.Message.Header.POST:
                                send = Task.Factory.StartNew(() => Send(msg, obj.id));
                                break;
                            case chatLib.Message.Header.FILE:
                                send = Task.Factory.StartNew(() => Send(msg, obj.id));
                                break;
                        }
                        
                    }
                    else
                    {
                        switch (msg.Head)
                        {
                            case chatLib.Message.Header.REGISTER:
                                chatLib.Message message = new chatLib.Message(chatLib.Message.Header.REGISTER);
                                if (users.ContainsKey(msg.MessageList[0]))
                                {

                                    message.addData("error");
                                }
                                else
                                {
                                    users.Add(msg.MessageList[0],MD5Sifrele(msg.MessageList[1]));
                                    message.addData("success");

                                    string text = "";
                                    foreach (DictionaryEntry item in users)
                                    {
                                        text += item.Key.ToString() + "|" + item.Value.ToString() + ";";

                                    }

                                    text = text.Trim(';');

                                    File.WriteAllText(AppDomain.CurrentDomain.BaseDirectory + "users.txt", text);
                                }
                                send.ContinueWith(antecendent => SendOne(message, obj.id));
                                break;
                            case chatLib.Message.Header.JOIN:
                                chatLib.Message message1 = new chatLib.Message(chatLib.Message.Header.JOIN);
                                if (users.ContainsKey(msg.MessageList[0]))
                                {
                                    if (users[msg.MessageList[0]].Equals(MD5Sifrele(msg.MessageList[1])))
                                    {
                                        message1.addData("success");
                                        message1.addData(msg.MessageList[0]);
                                    }
                                    message1.addData("error");
                                }
                                else
                                {
                                    message1.addData("error");
                                }

                                send.ContinueWith(antecendent => SendOne(message1, obj.id));
                                break;
                            case chatLib.Message.Header.POST:
                                send.ContinueWith(antecendent => Send(msg, obj.id));
                                break;
                            case chatLib.Message.Header.FILE:
                                send.ContinueWith(antecendent => Send(msg, obj.id));
                                break;
                        }
                        
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

        //Okuma
        private void Connection(MyClient obj)
        {
            list.TryAdd(obj.id, obj);
            LogWrite(String.Format("[/ Client {0} connected /]", obj.id));
            if (obj.stream.CanRead && obj.stream.CanWrite)
            {
                while (obj.client.Connected)
                {
                    try
                    {

                        obj.stream.BeginRead(obj.buffer, 0, obj.buffer.Length, new AsyncCallback(Read), obj);
                        obj.handle.WaitOne();
                    }
                    catch (IOException e) { LogWrite(String.Format("[/ {0} /]", e.Message)); }
                    catch (ObjectDisposedException e) { LogWrite(String.Format("[/ {0} /]", e.Message)); }
                }
            }
            else
            {
                LogWrite(String.Format("[/ Client {0} stream cannot read/write /]", obj.id));
            }
            obj.client.Close();
            list.TryRemove(obj.id, out MyClient tmp);
            LogWrite(String.Format("[/ Client {0} connection closed /]", obj.id));
        }

        private void Listener(IPAddress localaddr, Int32 port)
        {
            try
            {
                TcpListener listener = new TcpListener(localaddr, port);
                listener.Start();
                Active(true);
                while (active)
                {
                    if (listener.Pending())
                    {
                        MyClient obj = new MyClient();
                        obj.id = id;
                        obj.client = listener.AcceptTcpClient();
                        obj.stream = obj.client.GetStream();
                        obj.buffer = new byte[obj.client.ReceiveBufferSize];
                        obj.data = new StringBuilder();
                        obj.handle = new EventWaitHandle(false, EventResetMode.AutoReset);
                        Thread th = new Thread(() => Connection(obj))
                        {
                            IsBackground = true
                        };
                        th.Start();
                        id++;
                    }
                    else
                    {
                        Thread.Sleep(500);
                    }
                }
                listener.Server.Close();
                Active(false);
            }
            catch (SocketException e) { LogWrite(String.Format("[/ {0} /]", e.Message)); }
        }

        private void StartButton_Click(object sender, EventArgs e)
        {
            if (active)
            {
                active = false;
            }
            else
            {
                if (listener == null || !listener.IsAlive)
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
                        listener = new Thread(() => Listener(localaddr, port))
                        {
                            IsBackground = true
                        };
                        listener.Start();
                    }
                }
            }
        }

        //Client e mesaj yolla
        private void Write(IAsyncResult result)
        {
            MyClient obj = (MyClient)result.AsyncState;
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

        private void Send(chatLib.Message msg, long id = -1)
        {
            foreach (KeyValuePair<long, MyClient> obj in list)
            {
                if (id != obj.Value.id)
                {
                    try
                    {
                        IFormatter formatter = new BinaryFormatter();
                        MemoryStream memStream = new MemoryStream();
                        formatter.Serialize(memStream, msg);
                        byte[] buffer = memStream.GetBuffer();

                        obj.Value.stream.BeginWrite(buffer, 0, buffer.Length, new AsyncCallback(Write), obj.Value);
                    }
                    catch (IOException e) { LogWrite(String.Format("[/ {0} /]", e.Message)); }
                    catch (ObjectDisposedException e) { LogWrite(String.Format("[/ {0} /]", e.Message)); }
                }
            }
        }

        private void SendOne(chatLib.Message msg, long id)
        {
            foreach (KeyValuePair<long, MyClient> obj in list)
            {
                if (id == obj.Value.id)
                {
                    try
                    {
                        IFormatter formatter = new BinaryFormatter();
                        MemoryStream memStream = new MemoryStream();
                        formatter.Serialize(memStream, msg);
                        byte[] buffer = memStream.GetBuffer();

                        obj.Value.stream.BeginWrite(buffer, 0, buffer.Length, new AsyncCallback(Write), obj.Value);
                        break;
                    }
                    catch (IOException e) { LogWrite(String.Format("[/ {0} /]", e.Message)); }
                    catch (ObjectDisposedException e) { LogWrite(String.Format("[/ {0} /]", e.Message)); }
                }
            }
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
                    LogWrite("<- Server (You) -> " + msg);
                    chatLib.Message message = new chatLib.Message(chatLib.Message.Header.POST, msg);
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

        private void Disconnect()
        {
            foreach (KeyValuePair<long, MyClient> obj in list)
            {
                obj.Value.client.Close();
            }
        }

        private void DisconnectButton_Click(object sender, EventArgs e)
        {
            if (disconnect == null || !disconnect.IsAlive)
            {
                disconnect = new Thread(() => Disconnect())
                {
                    IsBackground = true
                };
                disconnect.Start();
            }
        }

        private void Server_FormClosing(object sender, FormClosingEventArgs e)
        {
            exit = true;
            active = false;
            if (disconnect == null || !disconnect.IsAlive)
            {
                disconnect = new Thread(() => Disconnect())
                {
                    IsBackground = true
                };
                disconnect.Start();
            }
        }

        private void ClearButton_Click(object sender, EventArgs e)
        {
            LogWrite();
        }

        private void sendTextBox_TextChanged(object sender, EventArgs e)
        {

        }

        private void Server_Load(object sender, EventArgs e)
        {
            sendTextBox.Visible = false;

        }
    }
}
