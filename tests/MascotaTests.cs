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
        // Task 1: vida de los Pokemon
        Eq(Anim.Look, PetForm.SpriteAnim(Anim.Code), "SpriteAnim_CodeEsLook");
        Eq(Anim.Wave, PetForm.SpriteAnim(Anim.Wave), "SpriteAnim_WaveIgual");
        Eq(0f, PetForm.WaddleAngle(false, false, true, 1), "Waddle_Parado");
        Eq(0f, PetForm.WaddleAngle(true, false, false, 1), "Waddle_Pared");
        Eq(0f, PetForm.WaddleAngle(true, true, true, 1), "Waddle_Pausa");
        Eq(-4f, PetForm.WaddleAngle(true, false, true, 0), "Waddle_Paso0");
        Eq(4f, PetForm.WaddleAngle(true, false, true, 1), "Waddle_Paso1");
        Eq(0f, PetForm.WaveTilt(0), "WaveTilt_Inicio");
        Eq(-0.15, PetForm.YawnSquash(30, 70), "Yawn_Estira");
        Eq(true, PetForm.YawnSquash(55, 70) > 0, "Yawn_Suelta");
        Eq(0.0, PetForm.YawnSquash(70, 70), "Yawn_Fin");
        Eq(true, PetForm.LookFlipped(45, 90), "Look_Centro");
        Eq(false, PetForm.LookFlipped(10, 90), "Look_Inicio");
        Eq(true, PetForm.WaveHopAt(2) && PetForm.WaveHopAt(26), "WaveHop_Si");
        Eq(false, PetForm.WaveHopAt(3), "WaveHop_No");

        Console.WriteLine(fallos == 0 ? "TODOS OK" : fallos + " FALLOS");
        return fallos;
    }
}
