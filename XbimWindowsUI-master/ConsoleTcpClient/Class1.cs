using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Xbim.Presentation;

namespace ConsoleTcpClient
{
    public class ConsoleTCPClient
	{
		public enum spxCommand { askGUID, locGUID, addGUID, noneMsg };

		int _port = 0;
		static IfcActionPanel _gui;
		private TcpClient tcpclnt;
		private static Stream stm;


		public ConsoleTCPClient(int port, IfcActionPanel gui)
		{
			_port = port;
			_gui = gui;
			tcpclnt = new TcpClient();
			tcpclnt.Connect("localhost", _port);
			stm = tcpclnt.GetStream();

			string str = String.Format("Hello message sent at {0}", DateTime.Now.ToString());
			ASCIIEncoding asen = new ASCIIEncoding();
			byte[] hello = asen.GetBytes(str);
			stm.Write(hello, 0, hello.Length);
			gui.tcpMsg = str;
		}

		public void StartClient()
		{
			StartReading();
		}

		public static void ReadData(IfcActionPanel gui, spxCommand command = spxCommand.noneMsg)
		{
			ASCIIEncoding asen = new ASCIIEncoding();

			while (true)
			{
				string msgRet = string.Empty;
				byte[] bb = new byte[100];
				string answer;
				int k = stm.Read(bb, 0, 100);

                if (k != 0)
                {
                    msgRet = Encoding.ASCII.GetString(bb);

                    if (msgRet.Contains(spxCommand.askGUID.ToString()))
                    {
                        gui.tcpMsg = "< askGUID";
                        answer = gui.RquestAskGUID();

                        byte[] ba = asen.GetBytes(answer);
                        stm.Write(ba, 0, ba.Length);
                        gui.tcpMsg = (answer.Contains("not found")) ? "> askGUID-błąd" : "> askGUID-OK";

                        Debug.WriteLine(string.Format("xbimTcp: Answering for askGUID with: {0}", answer));
                    }
                    else if (msgRet.Contains(spxCommand.locGUID.ToString()))
                    {
                        gui.tcpMsg = "< locGUID";
                        answer = gui.RequestLocGUID(msgRet);
                        byte[] ba = asen.GetBytes(answer);
                        stm.Write(ba, 0, ba.Length);
                        gui.tcpMsg = (answer.Contains("not found")) ? "> locGUID-nie znaleziono" : "> locGUID-OK";

                        Debug.WriteLine(string.Format("xbimTcp: Answering for locGUID with: {0}", answer));
                    }
                    else if (msgRet.Contains("open"))
                    {
                        gui.tcpMsg = "< Open";
                        answer = gui.RequestOpenFile(msgRet);
                        byte[] ba = asen.GetBytes(answer);
                        stm.Write(ba, 0, ba.Length);
                        gui.tcpMsg = (answer.Contains("not found")) ? "> Open-nie znaleziono" : "> Open-OK";

                        Debug.WriteLine(string.Format("xbimTcp: Answering for Open with: {0}", answer));
                    }
                }
			}
		}

		private static async void StartReading()
		{
			await Task.Run(() => ReadData(_gui));
		}
	}
}
