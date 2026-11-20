// Tests de las funciones puras de mascota.cs. Se compilan junto a mascota.cs con /main:MascotaTests
// (ver tests\run-tests.bat), asi no hace falta abrir la ventana.
using System;
using System.Drawing;

static class MascotaTests {
    static int fallos;

    static void Eq(object esperado, object real, string nombre) {
        if (Equals(esperado, real)) Console.WriteLine("OK " + nombre);
        else { fallos++; Console.WriteLine("FALLO " + nombre + ": esperado " + esperado + ", real " + real); }
    }

    static int Main() {
        Eq(Acc.Fiesta, PetForm.AutoAcc(new DateTime(2026, 12, 31)), "AutoAcc_Nochevieja");
        Console.WriteLine(fallos == 0 ? "TODOS OK" : fallos + " FALLOS");
        return fallos;
    }
}
