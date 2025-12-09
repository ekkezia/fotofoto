using UnityEngine;

public class PrintOnClick : MonoBehaviour
{
    public Printer printer;

    public void PrintNow()
    {
        printer.PrintText("Hello — printed from the button!");
    }
}
