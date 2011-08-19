using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.Net.Sockets;

namespace SkeletalViewer
{
    class I4C3D
    {
        private TcpClient tcpSender;
        private NetworkStream stream;

        private UdpClient udpSender;

        private string HostName { get; set; }
        private ushort PortNoTCP { get; set; }
        private ushort PortNoUDP { get; set; }

        /*
         * portNoTCP: I4C3Dコマンド送信用
         * portNoUDP: KinectSign用
         */
        public I4C3D(string hostName, ushort portNoTCP, ushort portNoUDP)
        {
            IPAddress[] ipAddress = Dns.GetHostAddresses(hostName);

            // TCP
            tcpSender = new TcpClient(hostName, portNoTCP);
            stream = tcpSender.GetStream();

            // UDP
            udpSender = new UdpClient(portNoUDP);

            HostName = hostName;
            PortNoTCP = portNoTCP;
            PortNoUDP = portNoUDP;
        }

        ~I4C3D()
        {
            stream.Dispose();
            stream.Close();
            Console.WriteLine("NetworkStream is closed.");
            tcpSender.Close();
            Console.WriteLine("Disconnected.");
        }

        public void SendCommandTCP(String message)
        {
            //Console.WriteLine(message);
            try
            {
                byte[] sendData = Encoding.UTF8.GetBytes(message);
                if (!tcpSender.Connected)
                {
                    tcpSender.Connect(HostName, PortNoTCP);
                    stream.Dispose();
                    stream = tcpSender.GetStream();
                }
                stream.Write(sendData, 0, sendData.Length);
                stream.Flush();

            }
            catch (SocketException e)
            {
                Console.WriteLine(e.Message);
                if (stream != null)
                {
                    stream.Close();
                }
                if (tcpSender != null)
                {
                    tcpSender.Close();
                }
            }
        }

        public void SendCommandUDP(String message)
        {
            udpSender.Send(Encoding.UTF8.GetBytes(message), message.Length, HostName, PortNoUDP);
        }
    }
}
