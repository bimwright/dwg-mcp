using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.GraphicsInterface;

namespace Bimwright.Dwg.Plugin
{
    internal sealed class UnicodeStyleInfo
    {
        public ObjectId StyleId { get; set; }
        public bool StyleCreated { get; set; }
        public bool FontDownloaded { get; set; }
        public string FontPath { get; set; }
    }

    internal static class UnicodeStyleService
    {
        internal const string StyleName = "Bimwright_Unicode";
        internal const string FontFileName = "OpenSans-CondensedLight.ttf";
        internal const string FontTypeFace = "Open Sans Condensed Light";
        internal const string FontDownloadUrl =
            "https://github.com/googlefonts/opensans/raw/main/fonts/ttf/OpenSans-CondensedLight.ttf";
        internal const string FontFileSha256 =
            "BD09013EBB713EF8BDF17FD7BAC92E51A6FF2F2E6844F561AE0CDA2A457A9ED0";

        internal static UnicodeStyleInfo EnsureStyle(Database db)
        {
            var fontPath = EnsureFontFile(out bool fontDownloaded);

            bool styleCreated;
            ObjectId styleId;
            using (var tx = db.TransactionManager.StartTransaction())
            {
                var tst = (TextStyleTable)tx.GetObject(db.TextStyleTableId, OpenMode.ForWrite);
                if (tst.Has(StyleName))
                {
                    styleId = tst[StyleName];
                    var existing = (TextStyleTableRecord)tx.GetObject(styleId, OpenMode.ForWrite);
                    existing.FileName = fontPath;
                    existing.Font = new FontDescriptor(FontTypeFace, false, false, 0, 0);
                    existing.TextSize = 0;
                    styleCreated = false;
                }
                else
                {
                    var tsr = new TextStyleTableRecord
                    {
                        Name = StyleName,
                        FileName = fontPath,
                        Font = new FontDescriptor(FontTypeFace, false, false, 0, 0),
                        TextSize = 0
                    };
                    styleId = tst.Add(tsr);
                    tx.AddNewlyCreatedDBObject(tsr, true);
                    styleCreated = true;
                }
                tx.Commit();
            }

            return new UnicodeStyleInfo
            {
                StyleId = styleId,
                StyleCreated = styleCreated,
                FontDownloaded = fontDownloaded,
                FontPath = fontPath
            };
        }

        internal static int ApplyToTargets(
            Database db,
            IEnumerable<ObjectId> targetIds,
            ObjectId styleId,
            string layerName = null)
        {
            if (targetIds == null) return 0;

            int reassigned = 0;
            using (var tx = db.TransactionManager.StartTransaction())
            {
                foreach (var id in targetIds.Where(i => !i.IsNull).Distinct())
                {
                    DBObject ent;
                    try { ent = tx.GetObject(id, OpenMode.ForWrite); }
                    catch { continue; }

                    reassigned += ApplyToObject(tx, ent, styleId);
                    McpLayerService.ApplyToObject(ent, layerName);
                }
                tx.Commit();
            }
            return reassigned;
        }

        internal static int ApplyToObject(Transaction tx, DBObject ent, ObjectId styleId)
        {
            switch (ent)
            {
                case AttributeReference att:
                    ReassignDbText(tx, att, styleId);
                    return 1;
                case DBText t:
                    ReassignDbText(tx, t, styleId);
                    return 1;
                case MText m:
                    double mh = ComputeTargetHeight(tx, m.TextStyleId, styleId, m.TextHeight);
                    m.TextStyleId = styleId;
                    m.TextHeight = mh;
                    return 1;
                case BlockReference br:
                    int count = 0;
                    foreach (ObjectId attId in br.AttributeCollection)
                    {
                        var attRef = (AttributeReference)tx.GetObject(attId, OpenMode.ForWrite);
                        ReassignDbText(tx, attRef, styleId);
                        count++;
                    }
                    return count;
                case MLeader ml:
                    double leaderHeight = ml.TextHeight > 0
                        ? ml.TextHeight
                        : (ml.MText?.TextHeight ?? 0);
                    double scaledLeaderHeight = ComputeTargetHeight(tx, ml.TextStyleId, styleId, leaderHeight);
                    ml.TextStyleId = styleId;
                    if (scaledLeaderHeight > 0)
                    {
                        ml.TextHeight = scaledLeaderHeight;
                        if (ml.MText != null)
                        {
                            var inner = ml.MText;
                            inner.TextStyleId = styleId;
                            inner.TextHeight = scaledLeaderHeight;
                            ml.MText = inner;
                        }
                    }
                    return 1;
                default:
                    return 0;
            }
        }

        internal static double ComputeTargetHeight(
            Transaction tx,
            ObjectId currentStyleId,
            ObjectId targetStyleId,
            double currentHeight)
        {
            double normalizedHeight = currentHeight > 0 ? currentHeight : 2.5;
            if (currentStyleId == targetStyleId)
            {
                return normalizedHeight;
            }

            double scale = ResolveHeightScale(tx, currentStyleId);
            return normalizedHeight * scale;
        }

        private static void ReassignDbText(Transaction tx, DBText t, ObjectId styleId)
        {
            double h = ComputeTargetHeight(tx, t.TextStyleId, styleId, t.Height);
            t.TextStyleId = styleId;
            t.Height = h;
        }

        private static double ResolveHeightScale(Transaction tx, ObjectId styleId)
        {
            if (styleId.IsNull)
            {
                return UnicodeScaleHeuristics.UnknownHeightScale;
            }

            try
            {
                var style = (TextStyleTableRecord)tx.GetObject(styleId, OpenMode.ForRead);
                return UnicodeScaleHeuristics.DetermineScaleFactor(
                    style.Name,
                    style.FileName,
                    style.Font.TypeFace);
            }
            catch
            {
                return UnicodeScaleHeuristics.UnknownHeightScale;
            }
        }

        private static string EnsureFontFile(out bool downloaded)
        {
            downloaded = false;

            var winFonts = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Fonts),
                FontFileName);
            if (File.Exists(winFonts)) return winFonts;

            var acadFonts = @"C:\Program Files\Autodesk\AutoCAD 2024\Fonts\" + FontFileName;
            if (File.Exists(acadFonts)) return acadFonts;

            var bundledFont = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "Fonts",
                FontFileName);
            if (File.Exists(bundledFont) && HasExpectedSha256(bundledFont, FontFileSha256))
                return bundledFont;

            var cacheDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Bimwright", "Fonts");
            var cachePath = Path.Combine(cacheDir, FontFileName);
            if (File.Exists(cachePath))
            {
                if (HasExpectedSha256(cachePath, FontFileSha256)) return cachePath;
                try { File.Delete(cachePath); } catch { }
            }

            Directory.CreateDirectory(cacheDir);
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            using (var wc = new WebClient())
            {
                wc.DownloadFile(FontDownloadUrl, cachePath);
            }
            if (!HasExpectedSha256(cachePath, FontFileSha256))
            {
                try { File.Delete(cachePath); } catch { }
                throw new InvalidOperationException("Downloaded OpenSans-CondensedLight.ttf failed SHA256 verification.");
            }
            downloaded = true;
            return cachePath;
        }

        private static bool HasExpectedSha256(string path, string expectedHex)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
            {
                var actual = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
                return string.Equals(actual, expectedHex, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
