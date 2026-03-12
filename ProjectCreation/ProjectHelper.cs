using FFXIVLooseTextureCompiler.Export;
using FFXIVLooseTextureCompiler.ImageProcessing;
using FFXIVLooseTextureCompiler.PathOrganization;
using FFXIVLooseTextureCompiler.Racial;
using FFXIVLooseTextureCompiler;
using LooseTextureCompilerCore.Racial;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static FFXIVLooseTextureCompiler.ImageProcessing.ImageManipulation;
using Penumbra.GameData.Enums;

namespace LooseTextureCompilerCore.ProjectCreation
{
    public static class ProjectHelper
    {
        static string[] _choiceTypes = new string[] { "Detailed", "Simple", "Dropdown", "Group Is Checkbox" };
        static string[] _bodyNames = new string[] { "Vanilla and Gen2", "BIBO+", "Gen3", "TBSE and HRBODY", "TAIL", "Otopop" };
        static string[] _bodyNamesSimplified = new string[] { "BIBO+", "Gen3", "TBSE and HRBODY", "Otopop" };
        static string[] _genders = new string[] { "Masculine", "Feminine" };
        static string[] _faceTypes = new string[] { "Face 1", "Face 2", "Face 3", "Face 4", "Face 5", "Face 6", "Face 7", "Face 8", "Face 9" };
        static string[] _faceParts = new string[] { "Face", "Eyebrows", "Eyes", "Ears", "Face Paint", "Hair", "Face B", "Etc B" };
        static string[] _faceScales = new string[] { "Vanilla Scales", "Scaleless Vanilla", "Scaleless Varied" };

        public static void ExportJson(string jsonFilePath)
        {
            string jsonText = @"{
  ""Name"": """",
  ""Priority"": 0,
  ""Files"": { },
  ""FileSwaps"": { },
  ""Manipulations"": []
}";
            if (jsonFilePath != null)
            {
                using (StreamWriter writer = new StreamWriter(jsonFilePath))
                {
                    writer.WriteLine(jsonText);
                }
            }
        }

        public static UVMapType SortUVTexture(TextureSet textureSet, string file)
        {
            bool foundStringIdentifier = false;
            UVMapType uVMapType = UVMapType.Base;
            if (file.ToLower().Contains("base"))
            {
                uVMapType = UVMapType.Base;
                foundStringIdentifier = true;
            }

            if (file.ToLower().Contains("norm"))
            {
                uVMapType = UVMapType.Normal;
                foundStringIdentifier = true;
            }

            if (file.ToLower().Contains("mask"))
            {
                uVMapType = UVMapType.Mask;
                foundStringIdentifier = true;
            }

            if (file.ToLower().Contains("glow"))
            {
                uVMapType = UVMapType.Glow;
                foundStringIdentifier = true;
            }



            if (!foundStringIdentifier)
            {
                uVMapType = ImageManipulation.UVMapTypeClassifier(file);
            }
            switch (uVMapType)
            {
                case UVMapType.Base:
                    textureSet.Base = file;
                    break;
                case UVMapType.Normal:
                    textureSet.Normal = file;
                    break;
                case UVMapType.Mask:
                    textureSet.Mask = file;
                    break;
                case UVMapType.Glow:
                    textureSet.Glow = file;
                    break;
            }
            return uVMapType;
        }
        public static void ExportMeta(string metaFilePath, string name, string author = "Loose Texture Compiler",
            string description = "Exported By Loose Texture Compiler", string modVersion = "0.0.0",
            string modWebsite = @"https://github.com/Sebane1/FFXIVLooseTextureCompiler")
        {
            string metaText = @"{
  ""FileVersion"": 3,
  ""Name"": """ + (!string.IsNullOrEmpty(name) ? name : "") + @""",
  ""Author"": """ + (!string.IsNullOrEmpty(author) ? author :
        "FFXIV Loose Texture Compiler") + @""",
  ""Description"": """ + (!string.IsNullOrEmpty(description) ? description :
        "Exported by FFXIV Loose Texture Compiler") + @""",
  ""Version"": """ + modVersion + @""",
  ""Website"": """ + modWebsite + @""",
  ""ModTags"": []
}";
            if (metaFilePath != null)
            {
                using (StreamWriter writer = new StreamWriter(metaFilePath))
                {
                    writer.WriteLine(metaText);
                }
            }
        }

        public static TextureSet CreateBodyTextureSet(Genders gender, BodyType baseBody, RaceInfo.RaceTypes race, int tail, bool uniqueAuRa = false)
        {
            return CreateBodyTextureSet((int)gender, (int)baseBody, (int)race, tail, uniqueAuRa);
        }
        public static TextureSet CreateBodyTextureSet(int gender, int baseBody, int race, int tail, bool uniqueAuRa = false)
        {
            TextureSet textureSet = new TextureSet();
            textureSet.TextureSetName = _bodyNames[baseBody] + (_bodyNames[baseBody].ToLower().Contains("tail") ? " " +
                (tail + 1) : "") + ", " + (race == 5 ? "Unisex" : _genders[gender])
                + ", " + RaceInfo.Races[race];
            AddBodyPaths(textureSet, gender, baseBody, race, tail, uniqueAuRa);
            return textureSet;
        }
        public static TextureSet CreateFaceTextureSet(FaceTypes faceType, FaceParts facePart, int faceExtra,
        Genders gender, RaceInfo.RaceTypes race, RaceInfo.SubRaceTypes subRace, FaceScales auraScales, bool asym)
        {
            return CreateFaceTextureSet((int)faceType, (int)facePart, faceExtra, (int)gender, (int)race, (int)subRace, (int)auraScales, asym);
        }

        public static TextureSet CreateFaceTextureSet(int faceType, int facePart, int faceExtra,
            int gender, int race, int subRace, int auraScales, bool asym)
        {
            TextureSet textureSet = new TextureSet();
            textureSet.TextureSetName = _faceParts[(int)facePart] + ((int)facePart == 4 ? " "
                + (faceExtra + 1) : "") + ", " + ((int)facePart != 4 ? _genders[(int)gender] : "Unisex")
                + ", " + ((int)facePart != 4 ? RaceInfo.SubRaces[(int)subRace] : "Multi Race") + ", "
                + ((int)facePart != 4 ? _faceTypes[(int)faceType] : "Multi Face");
            switch (facePart)
            {
                default:
                    AddFacePaths(textureSet, (int)subRace, (int)facePart, (int)faceType, (int)gender, (int)auraScales, asym);
                    break;
                case 2:
                    AddEyePaths(textureSet, (int)subRace, (int)faceType, (int)gender, (int)auraScales, asym);
                    break;
                case 4:
                    AddDecalPath(textureSet, faceExtra);
                    break;
                case 5:
                    AddHairPaths(textureSet, (int)gender, (int)facePart, faceExtra, (int)race, (int)subRace);
                    break;
            }
            textureSet.IgnoreMaskGeneration = true;
            if (facePart == 0)
            {
                BackupTexturePaths.AddFaceBackupPaths((int)gender, (int)subRace, faceExtra, textureSet);
            }
            return textureSet;
        }
        private static void AddBodyPaths(TextureSet textureSet, int gender, int baseBody, int race, int tail, bool uniqueAuRa = false)
        {
            if (race != 3 || baseBody != 6)
            {
                textureSet.InternalBasePath = RacePaths.GetBodyTexturePath(0, gender, baseBody, race, tail, uniqueAuRa);
            }
            textureSet.InternalNormalPath = RacePaths.GetBodyTexturePath(1, gender, baseBody, race, tail, uniqueAuRa);
            textureSet.InternalMaskPath = RacePaths.GetBodyTexturePath(2, gender, baseBody, race, tail, uniqueAuRa);
            textureSet.InternalMaterialPath = RacePaths.GetBodyMaterialPath(gender, baseBody, race, tail);
            BackupTexturePaths.AddBodyBackupPaths(gender, race, textureSet);
        }

        private static void AddDecalPath(TextureSet textureSet, int faceExtra)
        {
            textureSet.InternalBasePath = RacePaths.GetFaceTexturePath(faceExtra);
        }
        public static void ExportProject(string path, string name, List<TextureSet> exportTextureSets, TextureProcessor textureProcessor, 
            string xNormalPath = "", int generationType = 3, bool generateNormals = false, bool generateMulti = false, bool finalize = true)
        {
            List<TextureSet> textureSets = new List<TextureSet>();
            string jsonFilepath = Path.Combine(path, "default_mod.json");
            string metaFilePath = Path.Combine(path, "meta.json");
            foreach (TextureSet item in exportTextureSets)
            {
                if (item.OmniExportMode)
                {
                    UniversalTextureSetCreator.ConfigureTextureSet(item);
                }
                textureSets.Add(item);
            }
            Directory.CreateDirectory(path);
            textureProcessor.CleanGeneratedAssets(path);
            textureProcessor.Export(textureSets, new Dictionary<string, int>(), path, generationType, generateNormals, generateMulti, File.Exists(xNormalPath) && finalize, xNormalPath);
            ProjectHelper.ExportJson(jsonFilepath);
            ProjectHelper.ExportMeta(metaFilePath, name);
        }
        private static void AddHairPaths(TextureSet textureSet, int gender, int facePart, int faceExtra, int race, int subrace)
        {
            textureSet.TextureSetName = _faceParts[facePart] + " " + (faceExtra + 1)
                + ", " + _genders[gender] + ", " + RaceInfo.Races[race];

            textureSet.InternalNormalPath = RacePaths.GetHairTexturePath(1, faceExtra,
                gender, race, subrace);

            textureSet.InternalMaskPath = RacePaths.GetHairTexturePath(2, faceExtra,
                gender, race, subrace);
        }

        public static void AddEyePaths(TextureSet textureSet, int subrace, int faceType, int gender, int auraScales, bool asym)
        {
            RaceEyePaths.GetEyeTextureSet(subrace, faceType, gender == 1, textureSet);
        }

        public static void AddFacePaths(TextureSet textureSet, int subrace, int facePart, int faceType, int gender, int auraScales, bool asym)
        {
            if (facePart != 1)
            {
                textureSet.InternalBasePath = RacePaths.GetFacePath(0, gender, subrace,
                    facePart, faceType, auraScales, asym);
            }

            textureSet.InternalNormalPath = RacePaths.GetFacePath(1, gender, subrace,
            facePart, faceType, auraScales, asym);

            textureSet.InternalMaskPath = RacePaths.GetFacePath(2, gender, subrace,
            facePart, faceType, auraScales, asym);
        }
        public enum ChoiceTypes
        {
            Detailed = 0, Simple = 1, Dropdown = 2, GroupIsCheckbox = 3
        }
        public enum BodyType
        {
            VanillaAndGen2 = 0, BiboPlus = 1, Gen3 = 2, TBSEAndHRBODY = 3, TAIL = 4, Otopop = 5
        }

        public enum Genders
        {
            Masculine = 0, Feminine = 1
        }

        public enum FaceTypes
        {
            Face1 = 0, Face2 = 1, Face3 = 2, Face4 = 3, Face5 = 4, Face6 = 5, Face7 = 6, Face8 = 7, Face9 = 8
        }

        public enum FaceParts
        {
            Face = 0, Eyebrows = 1, Eyes = 2, Ears = 3, FacePaint = 4, Hair = 5, FaceB = 6, EtcB = 7
        }

        public enum FaceScales
        {
            VanillaScales = 0, ScalelessVanilla = 1, ScalelessVaried = 2
        }
    }
}
