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

    // Bitmap transparente con rectangulos opacos
    static Bitmap Bmp(int w, int h, params Rectangle[] opacos) {
        Bitmap b = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(b)) {
            g.Clear(Color.Transparent);
            foreach (Rectangle r in opacos) g.FillRectangle(Brushes.Black, r);
        }
        return b;
    }

    static int Main() {
        Eq(Acc.Fiesta, PetForm.AutoAcc(new DateTime(2026, 12, 31)), "AutoAcc_Nochevieja");
        // Task 1: vida de los Pokemon
        Eq(Anim.Look, PetForm.SpriteAnim(Anim.Code), "SpriteAnim_CodeEsLook");
        Eq(Anim.Wave, PetForm.SpriteAnim(Anim.Wave), "SpriteAnim_WaveIgual");
        Eq(0f, PetForm.WaddleAngle(false, true, 1), "Waddle_Parado");
        Eq(0f, PetForm.WaddleAngle(true, false, 1), "Waddle_Pared");
        Eq(-4f, PetForm.WaddleAngle(true, true, 0), "Waddle_Paso0");
        Eq(4f, PetForm.WaddleAngle(true, true, 1), "Waddle_Paso1");
        // Revision final: no se queda inclinada al parar para una animacion, dormir o caer
        Eq(false, PetForm.WaddleMoving(true, false, Anim.Yawn, false, 0), "Waddle_Animacion");
        Eq(false, PetForm.WaddleMoving(true, false, Anim.None, true, 0), "Waddle_Dormida");
        Eq(false, PetForm.WaddleMoving(true, false, Anim.None, false, 3), "Waddle_Cayendo");
        Eq(false, PetForm.WaddleMoving(true, true, Anim.None, false, 0), "Waddle_Pausa");
        Eq(true, PetForm.WaddleMoving(true, false, Anim.None, false, 0), "Waddle_Andando");
        // Revision final: el gorro no se sale por arriba de la ventana
        Eq(8, PetForm.HatDrop(-7.2), "HatDrop_Fuera");
        Eq(0, PetForm.HatDrop(3), "HatDrop_Dentro");
        // Revision final: el bocadillo no salta con el contoneo (usa el angulo del modo, no la inclinacion)
        Eq(0f, PetForm.BubbleAngle(false, 30f, 0f), "Bubble_SueloIgnoraInclinacion");
        Eq(30f, PetForm.BubbleAngle(true, 30f, 0f), "Bubble_AireUsaGiro");
        Eq(90f, PetForm.BubbleAngle(false, 94f, 90f), "Bubble_Pared");
        Eq(0f, PetForm.WaveTilt(0), "WaveTilt_Inicio");
        Eq(-0.15, PetForm.YawnSquash(30, 70), "Yawn_Estira");
        Eq(true, PetForm.YawnSquash(55, 70) > 0, "Yawn_Suelta");
        Eq(0.0, PetForm.YawnSquash(70, 70), "Yawn_Fin");
        Eq(true, PetForm.LookFlipped(45, 90), "Look_Centro");
        Eq(false, PetForm.LookFlipped(10, 90), "Look_Inicio");
        Eq(true, PetForm.WaveHopAt(2) && PetForm.WaveHopAt(26), "WaveHop_Si");
        Eq(false, PetForm.WaveHopAt(3), "WaveHop_No");

        // Task 2: geometria de sombreros
        int[][] nav = PetForm.HatShape(Acc.Navidad);
        Eq(6, nav.Length, "HatShape_NavidadFilas");
        Eq("2,11,-1,0", string.Join(",", nav[0]), "HatShape_NavidadAla");
        Eq(null, PetForm.HatShape(Acc.Gafas), "HatShape_Gafas");
        Eq(null, PetForm.HatShape(Acc.Ninguno), "HatShape_Ninguno");
        Eq(Acc.Ninguno, PetForm.SpriteAcc(Acc.Gafas), "SpriteAcc_Gafas");
        Eq(Acc.Bruja, PetForm.SpriteAcc(Acc.Bruja), "SpriteAcc_Bruja");
        Eq(Rectangle.Empty, PetForm.HeadBox(Bmp(10, 10)), "HeadBox_Vacio");
        Bitmap oreja = Bmp(60, 60, new Rectangle(20, 30, 20, 30), new Rectangle(25, 10, 3, 20));
        Eq(new Rectangle(25, 10, 3, 10), PetForm.HeadBox(oreja), "HeadBox_Oreja");
        Bitmap redonda = Bmp(60, 60, new Rectangle(10, 5, 40, 55));
        Eq(new Rectangle(10, 5, 40, 10), PetForm.HeadBox(redonda), "HeadBox_Cabeza");
        Eq(3, PetForm.HatCell(3, 6), "HatCell_Min");
        Eq(6, PetForm.HatCell(60, 6), "HatCell_Justo");
        Eq(6, PetForm.HatCell(1000, 6), "HatCell_Max");
        Eq(3, PetForm.HatCol(10, true), "HatCol_Flip");
        Eq(10, PetForm.HatCol(10, false), "HatCol_Normal");
        Bitmap orejas = Bmp(60, 60, new Rectangle(10, 30, 40, 30), new Rectangle(12, 10, 6, 20), new Rectangle(42, 10, 6, 20));
        Eq(false, PetForm.HatFits(orejas, PetForm.HeadBox(orejas)), "HatFits_DosOrejas");
        Bitmap cuerno = Bmp(60, 60, new Rectangle(10, 30, 40, 30), new Rectangle(28, 5, 4, 25));
        Eq(false, PetForm.HatFits(cuerno, PetForm.HeadBox(cuerno)), "HatFits_UnCuerno");
        Eq(true, PetForm.HatFits(redonda, PetForm.HeadBox(redonda)), "HatFits_Redonda");
        Eq(false, PetForm.HatFits(redonda, Rectangle.Empty), "HatFits_Vacio");
        // Como Pikachu: orejas a distinta altura, en la fila central de la franja solo se ve una
        Bitmap pika = Bmp(60, 60, new Rectangle(0, 20, 60, 40), new Rectangle(2, 0, 6, 20), new Rectangle(40, 6, 6, 14));
        Eq(false, PetForm.HatFits(pika, PetForm.HeadBox(pika)), "HatFits_OrejasDesiguales");
        // Mechon pequeno sobre una cabeza ancha (como Jigglypuff): si lleva gorro
        Bitmap mechon = Bmp(60, 60, new Rectangle(5, 4, 50, 56), new Rectangle(28, 0, 6, 4));
        Eq(true, PetForm.HatFits(mechon, PetForm.HeadBox(mechon)), "HatFits_Mechon");

        Console.WriteLine(fallos == 0 ? "TODOS OK" : fallos + " FALLOS");
        return fallos;
    }
}
