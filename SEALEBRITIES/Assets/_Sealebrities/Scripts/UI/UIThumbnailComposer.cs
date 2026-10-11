using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// FinTube collage thumbnail (L.4, player feedback r): up to 5 animals of the video as tiles – 1 = full, 2 = split,
    /// 3 = one big + two small, 4 = 2 × 2, 5 = one big + four small. Each tile is today's best photo of that species
    /// (gallery) or, without a photo, its icon on a depth-coloured gradient. Style sticker: none (Honest), „WOW!“
    /// (Spicy), red arrow + „ECHT?!“ (Clickbait). thumbQuality = average tile quality (photo quality; icon tile 0.4)
    /// × (0.8 + 0.05 × tiles), max 1. Saved once per upload as thumbs/s{saveSeed}[_g]_day_{n}.png (512 × 288, max 60 kept; old day_{n}.png still load).
    public static class UIThumbnailComposer
    {
        public const int MaxTiles = 5;
        public const int Width = 512, Height = 288;
        const int MaxFiles = 60;

        public struct Tile
        {
            public SpeciesDef species;
            /// Loaded gallery photo (owned by the caller – destroy it when done) or null for the icon tile.
            public Texture2D photo;
            public float quality;
        }

        /// thumbs/ – automated test runs write into thumbs_autotest/ so they never overwrite the player's collages
        /// (a test game's day 2 would otherwise replace the player's own day_2.png).
        public static string Folder => Path.Combine(Application.persistentDataPath, GameManager.AutomatedRun ? "thumbs_autotest" : "thumbs");

        /// Normalized tile rects (x, y, w, h; y down) for 1–5 tiles.
        public static Rect TileRect(int count, int i)
        {
            const float g = 0.008f;   // gap
            switch (Mathf.Clamp(count, 1, MaxTiles))
            {
                case 1: return new Rect(0f, 0f, 1f, 1f);
                case 2: return i == 0 ? new Rect(0f, 0f, 0.5f - g, 1f) : new Rect(0.5f + g, 0f, 0.5f - g, 1f);
                case 3:
                    if (i == 0) return new Rect(0f, 0f, 0.62f - g, 1f);
                    return new Rect(0.62f + g, i == 1 ? 0f : 0.5f + g, 0.38f - g, 0.5f - g);
                case 4:
                    return new Rect((i % 2) * (0.5f + g), (i / 2) * (0.5f + g), 0.5f - g, 0.5f - g);
                default:
                    if (i == 0) return new Rect(0f, 0f, 0.56f - g, 1f);
                    int k = i - 1;
                    return new Rect(0.56f + g + (k % 2) * (0.22f + g), (k / 2) * (0.5f + g), 0.22f - g * 0.5f, 0.5f - g);
            }
        }

        /// Gradient colours by the species' preferred depth (shallow turquoise → deep navy).
        public static void DepthColors(SpeciesDef s, out Color top, out Color bottom)
        {
            float d = s != null ? Mathf.Clamp01(s.PrefDepth / 60f) : 0.2f;
            top = Color.Lerp(UIPalette.Rgb(0x8FE0EA), UIPalette.Rgb(0x2E5F93), d);
            bottom = Color.Lerp(UIPalette.Rgb(0x3FA9C4), UIPalette.Rgb(0x14284A), d);
        }

        /// Today's best photo of a species on this machine (journal best photo first, then the latest of today).
        public static PhotoEntry TodaysPhoto(SpeciesDef s, int day)
        {
            if (s == null) return null;
            var photos = Gallery.Photos;
            var e = Journal.Find(s.id);
            if (e != null && !string.IsNullOrEmpty(e.bestPhotoId))
                for (int i = photos.Count - 1; i >= 0; i--)
                    if (photos[i] != null && photos[i].file == e.bestPhotoId && photos[i].day == day) return photos[i];
            for (int i = photos.Count - 1; i >= 0; i--)
                if (photos[i] != null && photos[i].speciesId == s.id && photos[i].day == day) return photos[i];
            return null;
        }

        /// Photo quality used for a tile: the journal's best-photo quality when it is that photo, else 0.6; icon 0.4.
        public static float TileQuality(SpeciesDef s, PhotoEntry p)
        {
            if (p == null) return 0.4f;
            var e = s != null ? Journal.Find(s.id) : null;
            if (e != null && e.bestPhotoId == p.file && e.bestPhotoQuality > 0f) return Mathf.Clamp01(e.bestPhotoQuality);
            return 0.6f;
        }

        public static float Quality(Tile[] tiles, int count)
        {
            count = Mathf.Clamp(count, 0, MaxTiles);
            if (tiles == null || count == 0) return 0f;
            float sum = 0f;
            for (int i = 0; i < count; i++) sum += Mathf.Clamp01(float.IsNaN(tiles[i].quality) ? 0f : tiles[i].quality);
            return Mathf.Clamp01(sum / count * (0.8f + 0.05f * count));
        }

        // ------------------------------------------------------------------ UI element
        /// The collage as UI Toolkit elements (phone app). onTileClick(i) swaps that tile's species.
        public static VisualElement Build(Tile[] tiles, int count, TitleStyle style, float w, float h, Action<int> onTileClick)
        {
            var root = UIKit.Box();
            UIKit.Size(root, w, h);
            UIKit.Radius(root, Mathf.Min(w, h) * 0.08f);
            root.style.overflow = Overflow.Hidden;
            root.style.backgroundColor = UIPalette.Rgb(0x14284A);
            count = Mathf.Clamp(count, 0, MaxTiles);
            if (count == 0)
            {
                UIKit.Image(root, UIKit.VGradient(UIPalette.Rgb(0x2478AA), UIPalette.Rgb(0x5ECAD9)), false);
                return root;
            }
            for (int i = 0; i < count; i++)
            {
                var r = TileRect(count, i);
                var t = tiles[i];
                var tile = UIKit.Box();
                tile.style.position = Position.Absolute;
                tile.style.left = Length.Percent(r.x * 100f);
                tile.style.top = Length.Percent(r.y * 100f);
                tile.style.width = Length.Percent(r.width * 100f);
                tile.style.height = Length.Percent(r.height * 100f);
                tile.style.overflow = Overflow.Hidden;
                if (t.photo != null) UIKit.ImageCover(tile, t.photo);
                else
                {
                    DepthColors(t.species, out var top, out var bottom);
                    UIKit.Image(tile, UIKit.VGradient(bottom, top), false);
                    UIKit.Center(tile);
                    if (t.species != null)
                    {
                        var icon = UIWidgets.SpeciesIcon(t.species, true, r.width * w * 0.86f, r.height * h * 0.8f);
                        tile.Add(icon);
                    }
                }
                if (onTileClick != null)
                {
                    int idx = i;
                    tile.pickingMode = PickingMode.Position;
                    tile.RegisterCallback<ClickEvent>(_ => { UIAudio.Click(); onTileClick(idx); });
                }
                root.Add(tile);
            }
            AddSticker(root, style, w, h);
            return root;
        }

        static void AddSticker(VisualElement root, TitleStyle style, float w, float h)
        {
            if (style == TitleStyle.Honest) return;
            float s = Mathf.Min(w, h);
            if (style == TitleStyle.Spicy)
            {
                var wow = UIKit.Text(Loc.Tr("WOW!", "WOW!"), s * 0.2f, UIPalette.Rgb(0xF2C94C), true);
                UIKit.Outline(wow, Mathf.Max(1.5f, s * 0.012f), 0.4f);
                wow.style.position = Position.Absolute;
                wow.style.right = s * 0.05f; wow.style.top = s * 0.04f;
                wow.style.rotate = new Rotate(Angle.Degrees(12f));
                wow.pickingMode = PickingMode.Ignore;
                root.Add(wow);
            }
            else
            {
                var arrow = new UIGlyph(UIGlyphs.ArrowUp, UIPalette.Rgb(0xE0352B), s * 0.34f);
                arrow.style.position = Position.Absolute;
                arrow.style.left = w * 0.36f; arrow.style.bottom = s * 0.02f;
                arrow.style.rotate = new Rotate(Angle.Degrees(-35f));
                root.Add(arrow);
                var real = UIKit.Text(Loc.Tr("ECHT?!", "REAL?!"), s * 0.16f, Color.white, true);
                UIKit.Outline(real, Mathf.Max(1.5f, s * 0.012f), 0.4f);
                real.style.backgroundColor = UIPalette.Rgb(0xE0352B);
                UIKit.Pad(real, s * 0.01f, s * 0.04f, s * 0.01f, s * 0.04f);
                UIKit.Radius(real, s * 0.03f);
                real.style.position = Position.Absolute;
                real.style.right = s * 0.04f; real.style.top = s * 0.05f;
                real.style.rotate = new Rotate(Angle.Degrees(-6f));
                real.pickingMode = PickingMode.Ignore;
                root.Add(real);
            }
        }

        // ------------------------------------------------------------------ PNG
        /// Composes the collage on the GPU (512 × 288) and writes thumbs/s{saveSeed}[_g]_day_{day}.png. Returns the path or null.
        public static string Save(int day, Tile[] tiles, int count)
        {
            count = Mathf.Clamp(count, 0, MaxTiles);
            if (tiles == null || count == 0) return null;
            RenderTexture rt = null;
            Texture2D read = null;
            var prev = RenderTexture.active;
            try
            {
                Directory.CreateDirectory(Folder);
                rt = RenderTexture.GetTemporary(Width, Height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                RenderTexture.active = rt;
                GL.PushMatrix();
                GL.LoadPixelMatrix(0, Width, Height, 0);
                GL.Clear(true, true, UIPalette.Rgb(0x14284A));
                for (int i = 0; i < count; i++)
                {
                    var n = TileRect(count, i);
                    var r = new Rect(n.x * Width, n.y * Height, n.width * Width, n.height * Height);
                    var t = tiles[i];
                    DepthColors(t.species, out var top, out var bottom);
                    Graphics.DrawTexture(r, UIKit.VGradient(bottom, top));
                    Texture tex = t.photo != null ? (Texture)t.photo : t.species != null ? UICreatureIcons.Get(t.species) : null;
                    if (tex == null) continue;
                    if (t.photo != null) Graphics.DrawTexture(r, tex, Cover(tex, r), 0, 0, 0, 0);
                    else
                    {
                        // icon: contain inside 86 % of the tile, keep aspect
                        float aspect = tex.width / (float)Mathf.Max(1, tex.height);
                        float iw = r.width * 0.86f, ih = iw / aspect;
                        if (ih > r.height * 0.8f) { ih = r.height * 0.8f; iw = ih * aspect; }
                        Graphics.DrawTexture(new Rect(r.center.x - iw * 0.5f, r.center.y - ih * 0.5f, iw, ih), tex);
                    }
                }
                GL.PopMatrix();
                read = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                read.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
                read.Apply(false);
                byte[] png = read.EncodeToPNG();
                // per save (saveSeed – co-op guests carry the host's seed) and per role: a guest's day-N collage never
                // overwrites its own solo save's day_N, and two windows on one PC never overwrite each other
                var gm = GameManager.I;
                int seed = gm != null && gm.State != null ? gm.State.saveSeed : 0;
                string name = "s" + seed.ToString("x8", System.Globalization.CultureInfo.InvariantCulture) + (Net.IsClient ? "_g" : "")
                              + "_day_" + day.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".png";
                string path = Path.Combine(Folder, name);
                File.WriteAllBytes(path, png);
                Prune();
                return path;
            }
            catch (Exception e)
            {
                Debug.LogWarning("Sealebrities UI: thumbnail not saved: " + e.Message);
                return null;
            }
            finally
            {
                RenderTexture.active = prev;
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                if (read != null) UnityEngine.Object.Destroy(read);
            }
        }

        /// Source rect (UV) that covers the target rect keeping the aspect (centre crop).
        static Rect Cover(Texture tex, Rect target)
        {
            float ta = target.width / Mathf.Max(1f, target.height);
            float sa = tex.width / (float)Mathf.Max(1, tex.height);
            if (sa > ta)
            {
                float w = ta / sa;
                return new Rect((1f - w) * 0.5f, 0f, w, 1f);
            }
            float h = sa / ta;
            return new Rect(0f, (1f - h) * 0.5f, 1f, h);
        }

        /// Keeps the newest MaxFiles collages.
        static void Prune()
        {
            try
            {
                // new names (s…_day_N.png) and the legacy day_N.png of older versions
                var a = Directory.GetFiles(Folder, "*_day_*.png");
                var b = Directory.GetFiles(Folder, "day_*.png");
                var files = new string[a.Length + b.Length];
                a.CopyTo(files, 0);
                b.CopyTo(files, a.Length);
                if (files.Length <= MaxFiles) return;
                Array.Sort(files, (a, b) => File.GetLastWriteTimeUtc(a).CompareTo(File.GetLastWriteTimeUtc(b)));
                for (int i = 0; i < files.Length - MaxFiles; i++) File.Delete(files[i]);
            }
            catch (Exception) { }
        }

        /// Loads a saved collage (null when the file is missing – e.g. the host's video on a guest's machine).
        public static Texture2D Load(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            try
            {
                if (!File.Exists(path)) return null;
                var t = new Texture2D(2, 2, TextureFormat.RGB24, false) { name = "SB_Thumb" };
                if (!t.LoadImage(File.ReadAllBytes(path), true)) { UnityEngine.Object.Destroy(t); return null; }
                return t;
            }
            catch (Exception) { return null; }
        }
    }
}
