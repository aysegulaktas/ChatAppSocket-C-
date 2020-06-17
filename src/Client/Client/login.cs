using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Client
{
    public partial class login : Form
    {



        public Client client { get; set; }

        public login()
        {
            InitializeComponent();
        }

        public void MessageHandler(chatLib.Message msg)
        {
            if(msg.Head == chatLib.Message.Header.JOIN && msg.MessageList[0].Equals("success"))
            {
                MessageBox.Show("Giriş Başarılı");
                client.msgEvent -= new Client.msgDelegate(MessageHandler);
                client.tbMesajGonder.Enabled = true;
                client.btnDosyaGonder.Enabled = true;
                client.kullaniciAdi = msg.MessageList[1];
                client.btnGirisYap.Enabled = false;
                client.Text = msg.MessageList[1];
                this.Close();
            }
            else if(msg.Head == chatLib.Message.Header.REGISTER && msg.MessageList[0].Equals("success"))
            {
                MessageBox.Show("Kayıt Başarılı");
            }
            else
            {
                MessageBox.Show("Başarısız");
            }
            
        }

        private void kaydet_Click(object sender, EventArgs e)
        {

            String kullaniciAdi = tbKullaniciAdi.Text;
            String sifre = tbSifre.Text;

            chatLib.Message message = new chatLib.Message(chatLib.Message.Header.REGISTER);
            message.addData(kullaniciAdi);
            message.addData(sifre);

            if (client.send == null || client.send.IsCompleted)
            {

                client.send = Task.Factory.StartNew(() => client.Send(message));
            }
            else
            {
                client.send.ContinueWith(antecendent => client.Send(message));
            }

        }

        private void giris_Click(object sender, EventArgs e)
        {
            String kullaniciAdi = tbKullaniciAdi.Text;
            String sifre = tbSifre.Text;

            chatLib.Message message = new chatLib.Message(chatLib.Message.Header.JOIN);
            message.addData(kullaniciAdi);
            message.addData(sifre);

            if (client.send == null || client.send.IsCompleted)
            {

                client.send = Task.Factory.StartNew(() => client.Send(message));
            }
            else
            {
                client.send.ContinueWith(antecendent => client.Send(message));
            }
        }
    }
}
