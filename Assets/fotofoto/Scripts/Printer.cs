using UnityEngine;
using System.Collections;
using System.Net.Sockets;
using System.Text;

public class Printer : MonoBehaviour
{
    [Header("Printer Settings")]
    public string printerIP = "172.22.151.166";   // default

    void Start()
    {
        // If the user didn't assign an IP in the inspector, use the default
        if (string.IsNullOrEmpty(printerIP))
        {
            printerIP = "172.22.151.166";
        }
    }

    void Update()
    {
        // Example: Press A to print a screenshot
        if (OVRInput.GetDown(OVRInput.Button.One))
        {
            StartCoroutine(CaptureAndPrint());
        }
    }

    public void PrintText(string text)
    {
        try
        {
            Debug.Log("[PRINT] Printing text to printer at IP: " + printerIP);

            TcpClient client = new TcpClient(printerIP, 9100);
            NetworkStream stream = client.GetStream();

            // PJL wrapper (good for HP JetDirect printers)
            string header = "\x1B%-12345X@PJL JOB\n@PJL ENTER LANGUAGE = PCL\n";
            string footer = "\n\x1B%-12345X@PJL EOJ\n";

            byte[] headerBytes = Encoding.ASCII.GetBytes(header);
            byte[] textBytes = Encoding.ASCII.GetBytes(text + "\n");
            byte[] footerBytes = Encoding.ASCII.GetBytes(footer);

            stream.Write(headerBytes, 0, headerBytes.Length);
            stream.Write(textBytes, 0, textBytes.Length);
            stream.Write(footerBytes, 0, footerBytes.Length);

            stream.Flush();
            stream.Close();
            client.Close();

            Debug.Log("[PRINT] Printed text: " + text);
        }
        catch (System.Exception e)
        {
            Debug.LogError("[PRINT] Print text error: " + e.Message);
        }
    }   

    public void PrintPNG(byte[] pngBytes)
    {
        try
        {
            TcpClient client = new TcpClient(printerIP, 9100);
            NetworkStream stream = client.GetStream();

            // PJL wrapper (good for HP JetDirect printers)
            string header = "\x1B%-12345X@PJL JOB\n@PJL ENTER LANGUAGE = PCL\n";
            string footer = "\n\x1B%-12345X@PJL EOJ\n";

            byte[] headerBytes = Encoding.ASCII.GetBytes(header);
            byte[] footerBytes = Encoding.ASCII.GetBytes(footer);

            stream.Write(headerBytes, 0, headerBytes.Length);
            stream.Write(pngBytes, 0, pngBytes.Length);
            stream.Write(footerBytes, 0, footerBytes.Length);

            stream.Flush();
            stream.Close();
            client.Close();

            Debug.Log("Printed PNG.");
        }
        catch (System.Exception e)
        {
            Debug.LogError("Print PNG error: " + e.Message);
        }
    }

    public IEnumerator CaptureAndPrint()
    {
        yield return new WaitForEndOfFrame();

        Texture2D tex = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
        tex.Apply();

        byte[] png = tex.EncodeToPNG();
        PrintPNG(png);
    }
}
