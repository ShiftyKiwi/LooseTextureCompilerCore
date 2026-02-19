using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection.Metadata;
using System.Security.Cryptography;
using System.Threading.Tasks;
using CoenM.ImageHash;
using CoenM.ImageHash.HashAlgorithms;
using FFXIVLooseTextureCompiler.ImageProcessing;
using FFXIVLooseTextureCompiler.PathOrganization;
using FFXIVLooseTextureCompiler.Racial;
using FFXIVVoicePackCreator.Json;
using LooseTextureCompilerCore;
using Newtonsoft.Json;
using Penumbra.GameData.Files;
using Penumbra.LTCImport.Dds;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.PixelFormats;
using static FFXIVLooseTextureCompiler.TextureProcessor;
using Color = System.Drawing.Color;
using Group = FFXIVVoicePackCreator.Json.Group;
using Path = System.IO.Path;
using Size = System.Drawing.Size;

namespace FFXIVLooseTextureCompiler {
    public class TextureProcessor {
        private Dictionary<string, TextureSet> _redirectionCache;
        private Dictionary<string, TextureSet> _mtrlCache;
        private Dictionary<string, Bitmap> _normalCache;
        private Dictionary<string, Bitmap> _maskCache;
        private Dictionary<string, Bitmap> _glowCache;
        private Dictionary<string, string> _xnormalCache;
        private XNormal _xnormal;
        private List<KeyValuePair<string, string>> _textureSetQueue;
        private int _fileCount;

        private bool _finalizeResults;
        private bool _generateNormals;
        private bool _generateMulti;

        string _basePath = "";
        int _exportCompletion = 0;
        private int _exportMax;
        private DifferenceHash _hashAlgorithm;

        public int ExportMax { get => _exportMax; }
        public int ExportCompletion { get => _exportCompletion; }
        public string BasePath { get => _basePath; set => _basePath = value; }

        public TextureProcessor(string basePath = null) {
            _basePath = !string.IsNullOrEmpty(basePath) ? basePath : GlobalPathStorage.OriginalBaseDirectory;
            OnProgressChange += delegate {
                _exportCompletion++;
            };
        }

        public event EventHandler OnProgressChange;
        public event EventHandler OnStartedProcessing;
        public event EventHandler OnLaunchedXnormal;
        public event EventHandler<string> OnProgressReport;
        public event EventHandler<string> OnError;

        private Bitmap GetMergedBitmap(string file) {
            if (file.Contains("gen3")) {
                object test = new object();
            }
            if (file.Contains("baseTexBaked") && (file.Contains("_d_") ||
                file.Contains("_g_") || file.Contains("_n_") || file.Contains("_m_"))) {
                string path1 = file.Replace("baseTexBaked", "alpha_baseTexBaked");
                string path2 = file.Replace("baseTexBaked", "rgb_baseTexBaked");
                if (File.Exists(path1) && File.Exists(path2)) {
                    Bitmap alpha = TexIO.ResolveBitmap(path1);
                    Bitmap rgb = TexIO.ResolveBitmap(path2);
                    Bitmap merged = ImageManipulation.MergeAlphaToRGB(alpha, rgb);
                    TexIO.SaveBitmap(merged, file);
                    try {
                        Task.Run(() => {
                            Thread.Sleep(5000);
                            File.Delete(path1);
                            File.Delete(path2);
                        });
                    } catch {

                    }
                    alpha.Dispose();
                    rgb.Dispose();
                    return merged;
                }
            }
            return TexIO.ResolveBitmap(file);
        }

        public ulong CreateHash(string path) {
            if (_hashAlgorithm == null) {
                _hashAlgorithm = new DifferenceHash();
            }
            OnProgressReport?.Invoke(this, "Preparing " + Path.GetFileNameWithoutExtension(path));
            var image = TexIO.ResolveBitmap(path);
            OnProgressReport?.Invoke(this, "Scaling " + Path.GetFileNameWithoutExtension(path));
            var resized = TexIO.Resize(image, 100, 100);
            OnProgressReport?.Invoke(this, "Translating " + Path.GetFileNameWithoutExtension(path));
            var imageSharped = TexIO.BitmapToImageSharp(resized);
            OnProgressReport?.Invoke(this, "Hashing " + Path.GetFileNameWithoutExtension(path));
            var hash = _hashAlgorithm.Hash(imageSharped);
            OnProgressReport?.Invoke(this, "Hash Calculated");
            return hash;
        }
        public void BatchTextureSet(TextureSet parent, TextureSet child) {
            OnProgressReport?.Invoke(this, "XNormal Batch " + parent.TextureSetName);
            if (!string.IsNullOrEmpty(child.FinalBase)) {
                // Create a hash algorithm
                var hash = CreateHash(parent.FinalBase);

                if (!parent.Hashes.ContainsKey(child.FinalBase) || hash != parent.Hashes[child.FinalBase]) {
                    OnProgressReport?.Invoke(this, "Add To XNormal");
                    AddToXnormalPool(parent, child, XNormalTextureType.Base);
                    if (_finalizeResults) {
                        parent.Hashes[child.FinalBase] = hash;
                    }
                }
            }
            if (!string.IsNullOrEmpty(child.FinalNormal)) {
                // Create a hash algorithm
                var hash = CreateHash(parent.FinalNormal);

                if (!parent.Hashes.ContainsKey(child.FinalNormal) || hash != parent.Hashes[child.FinalNormal]) {
                    OnProgressReport?.Invoke(this, "Add To XNormal");
                    AddToXnormalPool(parent, child, XNormalTextureType.Normal);
                    if (_finalizeResults) {
                        parent.Hashes[child.FinalNormal] = hash;
                    }
                }
            }
            if (!string.IsNullOrEmpty(child.FinalMask)) {
                // Create a hash algorithm
                var hash = CreateHash(parent.FinalMask);

                if (!parent.Hashes.ContainsKey(child.FinalMask) || hash != parent.Hashes[child.FinalMask]) {
                    OnProgressReport?.Invoke(this, "Add To XNormal");
                    AddToXnormalPool(parent, child, XNormalTextureType.Mask);
                    if (_finalizeResults) {
                        parent.Hashes[child.FinalMask] = hash;
                    }
                }
            }
            if (!string.IsNullOrEmpty(child.Glow)) {
                // Create a hash algorithm
                var hash = CreateHash(parent.Glow);

                if (!parent.Hashes.ContainsKey(child.Glow) || hash != parent.Hashes[child.Glow]) {
                    OnProgressReport?.Invoke(this, "Add To XNormal");
                    AddToXnormalPool(parent, child, XNormalTextureType.Glow);
                    if (_finalizeResults) {
                        parent.Hashes[child.Glow] = hash;
                    }
                }
            }
        }
        public enum XNormalTextureType {
            Base, Normal, Mask, Glow
        }
        public void AddToXnormalPool(TextureSet parent, TextureSet child, XNormalTextureType xNormalTextureType) {
            string parentTexturePath = "";
            string childTexturePath = "";
            string internalPath = "";
            switch (xNormalTextureType) {
                case XNormalTextureType.Base:
                    parentTexturePath = parent.FinalBase;
                    childTexturePath = child.FinalBase;
                    internalPath = parent.InternalBasePath;
                    break;
                case XNormalTextureType.Normal:
                    parentTexturePath = parent.FinalNormal;
                    childTexturePath = child.FinalNormal;
                    internalPath = parent.InternalNormalPath;
                    break;
                case XNormalTextureType.Mask:
                    parentTexturePath = parent.FinalMask;
                    childTexturePath = child.FinalMask;
                    internalPath = parent.InternalMaskPath;
                    break;
                case XNormalTextureType.Glow:
                    parentTexturePath = parent.Glow;
                    childTexturePath = child.Glow;
                    internalPath = parent.InternalNormalPath;
                    break;
            }

            if (!_xnormalCache.ContainsKey(childTexturePath)) {
                string baseTextureAlpha = ImageManipulation.ReplaceExtension(
                ImageManipulation.AddSuffix(parentTexturePath, "_alpha"), ".png");
                string baseTextureRGB = ImageManipulation.ReplaceExtension(
                ImageManipulation.AddSuffix(parentTexturePath, "_rgb"), ".png");
                if (_finalizeResults || !File.Exists(childTexturePath.Replace("baseTexBaked", "rgb_baseTexBaked"))
                    || !File.Exists(childTexturePath.Replace("baseTexBaked", "alpha_baseTexBaked"))) {
                    if (childTexturePath.Contains("baseTexBaked")) {
                        _xnormalCache.Add(childTexturePath, childTexturePath);
                        Bitmap baseTexture = TexIO.ResolveBitmap(parentTexturePath);
                        if (Directory.Exists(Path.GetDirectoryName(baseTextureAlpha))
                            && Directory.Exists(Path.GetDirectoryName(baseTextureRGB))) {
                            string childAlpha = childTexturePath.Replace("baseTexBaked", "alpha");
                            string childRGB = childTexturePath.Replace("baseTexBaked", "rgb");
                            TexIO.SaveBitmap(ImageManipulation.ExtractTransparency(baseTexture), baseTextureAlpha);
                            TexIO.SaveBitmap(ImageManipulation.ExtractRGB(baseTexture), baseTextureRGB);
                            if (_finalizeResults) {
                                _xnormal.AddToBatch(internalPath, baseTextureAlpha, childAlpha, false);
                                _xnormal.AddToBatch(internalPath, baseTextureRGB, childRGB, xNormalTextureType == XNormalTextureType.Normal);
                            } else {
                                if (!File.Exists(ImageManipulation.AddSuffix(childTexturePath, "_baseTexBaked"))) {
                                    if (!File.Exists(childAlpha)) {
                                        new Bitmap(1024, 1024).Save(ImageManipulation.AddSuffix(childAlpha, "_baseTexBaked"), ImageFormat.Png);
                                    }
                                    if (!File.Exists(childRGB)) {
                                        new Bitmap(1024, 1024).Save(ImageManipulation.AddSuffix(childRGB, "_baseTexBaked"), ImageFormat.Png);
                                    }
                                }
                            }
                        } else {
                            //MessageBox.Show("Something has gone terribly wrong. " + parent.Base + "is missing");
                        }
                    }
                }
            }
        }
        public void Export(List<TextureSet> textureSetList, Dictionary<string, int> groupOptionTypes,
            string modPath, int generationType, bool generateNormals,
            bool generateMulti, bool useXNormal, string xNormalPathOverride = "") {
            Dictionary<string, List<TextureSet>> groups = new Dictionary<string, List<TextureSet>>();
            try {
                int i = 0;
                _fileCount = 0;
                _finalizeResults = useXNormal;
                _normalCache?.Clear();
                _maskCache?.Clear();
                _glowCache?.Clear();
                _mtrlCache?.Clear();
                _xnormalCache?.Clear();
                _redirectionCache?.Clear();
                _normalCache = new Dictionary<string, Bitmap>();
                _maskCache = new Dictionary<string, Bitmap>();
                _glowCache = new Dictionary<string, Bitmap>();
                _xnormalCache = new Dictionary<string, string>();
                _redirectionCache = new Dictionary<string, TextureSet>();
                _mtrlCache = new Dictionary<string, TextureSet>();
                _xnormal = new XNormal();
                _xnormal.XNormalPathOverride = xNormalPathOverride;
                _xnormal.BasePathOverride = _basePath;
                _generateNormals = generateNormals;
                _generateMulti = generateMulti;
                _exportCompletion = 0;
                _exportMax = 0;
                _exportMax = (textureSetList.Count * 4) + textureSetList.Count;
                Dictionary<string, string> alreadyCalculatedBases = new Dictionary<string, string>();
                Dictionary<string, string> alreadyCalculatedNormals = new Dictionary<string, string>();
                Dictionary<string, string> alreadyCalculatedMasks = new Dictionary<string, string>();
                OnProgressReport?.Invoke(this, "Preparing Data");
                foreach (TextureSet textureSet in textureSetList) {
                    OnProgressReport?.Invoke(this, "Merging Layers " + textureSet.TextureSetName);
                    if (!alreadyCalculatedBases.ContainsKey(textureSet.FinalBase) &&
                        (!string.IsNullOrEmpty(textureSet.Base) || textureSet.BaseOverlays.Count > 0)) {
                        List<string> images = new List<string>();
                        images.Add(textureSet.Base);
                        images.AddRange(textureSet.BaseOverlays);
                        ImageManipulation.MergeImageLayers(images, textureSet.FinalBase);
                        alreadyCalculatedBases[textureSet.FinalBase] = "";
                    }

                    if (!alreadyCalculatedNormals.ContainsKey(textureSet.FinalNormal) &&
                        (!string.IsNullOrEmpty(textureSet.Normal) || textureSet.NormalOverlays.Count > 0)) {
                        List<string> images = new List<string>();
                        images.Add(textureSet.Normal);
                        images.AddRange(textureSet.NormalOverlays);
                        ImageManipulation.MergeImageLayers(images, textureSet.FinalNormal);
                        alreadyCalculatedNormals[textureSet.FinalNormal] = "";
                    }

                    if (!alreadyCalculatedMasks.ContainsKey(textureSet.FinalMask) &&
                        (!string.IsNullOrEmpty(textureSet.Mask) || textureSet.MaskOverlays.Count > 0)) {
                        List<string> images = new List<string>();
                        images.Add(textureSet.Mask);
                        images.AddRange(textureSet.MaskOverlays);
                        ImageManipulation.MergeImageLayers(images, textureSet.FinalMask);
                        alreadyCalculatedMasks[textureSet.FinalMask] = "";
                    }

                    if (!groups.ContainsKey(textureSet.GroupName)) {
                        groups.Add(textureSet.GroupName, new List<TextureSet>() { textureSet });
                        foreach (TextureSet childSet in textureSet.ChildSets) {
                            childSet.GroupName = textureSet.GroupName;
                            groups[textureSet.GroupName].Add(childSet);
                            BatchTextureSet(textureSet, childSet);
                            _exportMax += 4;
                        }
                    } else {
                        groups[textureSet.GroupName].Add(textureSet);
                        foreach (TextureSet childSet in textureSet.ChildSets) {
                            childSet.GroupName = textureSet.GroupName;
                            groups[textureSet.GroupName].Add(childSet);
                            BatchTextureSet(textureSet, childSet);
                            _exportMax += 4;
                        }
                    }
                    OnProgressChange.Invoke(this, EventArgs.Empty);
                }
                if (_finalizeResults) {
                    if (OnLaunchedXnormal != null) {
                        OnLaunchedXnormal.Invoke(this, EventArgs.Empty);
                    }
                    _xnormal.ProcessBatches();
                }
                if (OnStartedProcessing != null) {
                    OnStartedProcessing.Invoke(this, EventArgs.Empty);
                }
                OnProgressReport?.Invoke(this, "Export To Penumbra");
                foreach (List<TextureSet> textureSets in groups.Values) {
                    int choiceOption = groupOptionTypes.ContainsKey(textureSets[0].GroupName)
                    ? (groupOptionTypes[textureSets[0].GroupName] == 0
                    ? generationType : groupOptionTypes[textureSets[0].GroupName] - 1)
                    : generationType;
                    Group group = new Group(textureSets[0].GroupName.Replace(@"/", "-").Replace(@"\", "-"), "", 0,
                        (choiceOption == 2 && textureSets.Count > 1) ? "Single" : "Multi", 0);
                    Option option = null;
                    Option baseTextureOption = null;
                    Option normalOption = null;
                    Option maskOption = null;
                    Option materialOption = null;
                    bool alreadySetOption = false;
                    foreach (TextureSet textureSet in textureSets) {
                        string textureSetHash = GetHashFromTextureSet(textureSet);
                        string baseTextureDiskPath = "";
                        string normalDiskPath = "";
                        string maskDiskPath = "";
                        string materialDiskPath = "";
                        bool skipTexExport = false;
                        if (_redirectionCache.ContainsKey(textureSetHash)) {
                            baseTextureDiskPath = GetDiskPath(_redirectionCache[textureSetHash].InternalBasePath, modPath, textureSetHash);
                            normalDiskPath = GetDiskPath(_redirectionCache[textureSetHash].InternalNormalPath, modPath, textureSetHash);
                            maskDiskPath = GetDiskPath(_redirectionCache[textureSetHash].InternalMaskPath, modPath, textureSetHash);
                            materialDiskPath = GetDiskPath(_redirectionCache[textureSetHash].InternalMaterialPath,
                                modPath,
                             (_redirectionCache[textureSetHash].InternalMaterialPath + textureSetHash + textureSet.InternalBasePath.GetHashCode().ToString() + textureSet.InternalNormalPath.GetHashCode().ToString() + textureSet.InternalMaskPath.GetHashCode().ToString()).GetHashCode().ToString());
                            skipTexExport = true;
                        } else {
                            baseTextureDiskPath = GetDiskPath(textureSet.InternalBasePath, modPath, textureSetHash);
                            normalDiskPath = GetDiskPath(textureSet.InternalNormalPath, modPath, textureSetHash);
                            maskDiskPath = GetDiskPath(textureSet.InternalMaskPath, modPath, textureSetHash);
                            materialDiskPath = GetDiskPath(textureSet.InternalMaterialPath,
                                modPath, (textureSet.InternalMaterialPath + textureSetHash + textureSet.InternalBasePath.GetHashCode().ToString() + textureSet.InternalNormalPath.GetHashCode().ToString() + textureSet.InternalMaskPath.GetHashCode().ToString()).GetHashCode().ToString());
                            _redirectionCache.Add(textureSetHash, textureSet);
                        }
                        switch (choiceOption) {
                            case 0:
                                if (!string.IsNullOrEmpty(textureSet.FinalBase) && !string.IsNullOrEmpty(textureSet.InternalBasePath)) {
                                    if (BaseLogic(textureSet, baseTextureDiskPath, skipTexExport)) {
                                        AddDetailedGroupOption(textureSet.InternalBasePath,
                                            baseTextureDiskPath.Replace(modPath + "\\", null), "Base", "", textureSet,
                                            textureSets, group, baseTextureOption, out baseTextureOption);
                                    } else {
                                        OnProgressChange.Invoke(this, EventArgs.Empty);
                                    }
                                } else {
                                    OnProgressChange.Invoke(this, EventArgs.Empty);
                                }
                                if (!string.IsNullOrEmpty(textureSet.InternalNormalPath)) {
                                    if (NormalLogic(textureSet, normalDiskPath, skipTexExport)) {
                                        AddDetailedGroupOption(textureSet.InternalNormalPath,
                                            normalDiskPath.Replace(modPath + "\\", null), "Normal", "", textureSet,
                                            textureSets, group, normalOption, out normalOption);
                                    } else {
                                        OnProgressChange.Invoke(this, EventArgs.Empty);
                                    }
                                } else {
                                    OnProgressChange.Invoke(this, EventArgs.Empty);
                                }
                                if (!string.IsNullOrEmpty(textureSet.InternalMaskPath)) {
                                    if (MaskLogic(textureSet, maskDiskPath, skipTexExport)) {
                                        AddDetailedGroupOption(textureSet.InternalMaskPath,
                                            maskDiskPath.Replace(modPath + "\\", null), "Mask", "", textureSet,
                                            textureSets, group, maskOption, out maskOption);
                                    } else {
                                        OnProgressChange.Invoke(this, EventArgs.Empty);
                                    }
                                } else {
                                    OnProgressChange.Invoke(this, EventArgs.Empty);
                                }
                                if ((!string.IsNullOrEmpty(textureSet.Material) || !string.IsNullOrEmpty(textureSet.Glow))
                                    && !string.IsNullOrEmpty(textureSet.InternalMaterialPath)) {
                                    if (MaterialLogic(textureSet, materialDiskPath, false)) {
                                        AddDetailedGroupOption(textureSet.InternalMaterialPath,
                                            materialDiskPath.Replace(modPath + "\\", null), "Material", "", textureSet,
                                            textureSets, group, materialOption, out materialOption);
                                    } else {
                                        OnProgressChange.Invoke(this, EventArgs.Empty);
                                    }
                                } else {
                                    OnProgressChange.Invoke(this, EventArgs.Empty);
                                }
                                break;
                            case 1:
                            case 2:
                            case 3:
                                if ((!textureSet.IsChildSet && choiceOption != 3) || (choiceOption == 3 && !alreadySetOption)) {
                                    if (!string.IsNullOrEmpty(textureSet.FinalBase) ||
                                        !string.IsNullOrEmpty(textureSet.FinalNormal) ||
                                        !string.IsNullOrEmpty(textureSet.FinalMask) ||
                                        !string.IsNullOrEmpty(textureSet.Glow) ||
                                        !string.IsNullOrEmpty(textureSet.Material)) {
                                        option = new Option(textureSet.TextureSetName == textureSet.GroupName || choiceOption == 3 ? "Enable"
                                        : textureSet.TextureSetName + (textureSet.ChildSets.Count > 0 ? " (Universal)" : ""), 0);
                                        group.Options.Add(option);
                                        alreadySetOption = true;
                                    }
                                }
                                if (!string.IsNullOrEmpty(textureSet.FinalBase) && !string.IsNullOrEmpty(textureSet.InternalBasePath)) {
                                    if (BaseLogic(textureSet, baseTextureDiskPath, skipTexExport)) {
                                        option.Files[textureSet.InternalBasePath] =
                                           baseTextureDiskPath.Replace(modPath + "\\", null);
                                    } else {
                                        OnProgressChange.Invoke(this, EventArgs.Empty);
                                    }
                                } else {
                                    OnProgressChange.Invoke(this, EventArgs.Empty);
                                }
                                if (!string.IsNullOrEmpty(textureSet.InternalNormalPath)) {
                                    if (NormalLogic(textureSet, normalDiskPath, skipTexExport)) {
                                        option.Files[textureSet.InternalNormalPath] =
                                            normalDiskPath.Replace(modPath + "\\", null);
                                    } else {
                                        OnProgressChange.Invoke(this, EventArgs.Empty);
                                    }
                                } else {
                                    OnProgressChange.Invoke(this, EventArgs.Empty);
                                }
                                if (!string.IsNullOrEmpty(textureSet.InternalMaskPath)) {
                                    if (MaskLogic(textureSet, maskDiskPath, skipTexExport)) {
                                        option.Files[textureSet.InternalMaskPath] =
                                           maskDiskPath.Replace(modPath + "\\", null);
                                    } else {
                                        OnProgressChange.Invoke(this, EventArgs.Empty);
                                    }
                                } else {
                                    OnProgressChange.Invoke(this, EventArgs.Empty);
                                }
                                if ((!string.IsNullOrEmpty(textureSet.Material) || !string.IsNullOrEmpty(textureSet.Glow))
                                    && !string.IsNullOrEmpty(textureSet.InternalMaterialPath)) {
                                    if (MaterialLogic(textureSet, materialDiskPath, false)) {
                                        option.Files[textureSet.InternalMaterialPath] =
                                           materialDiskPath.Replace(modPath + "\\", null);
                                    } else {
                                        OnProgressChange.Invoke(this, EventArgs.Empty);
                                    }
                                } else {
                                    OnProgressChange.Invoke(this, EventArgs.Empty);
                                }
                                break;
                        }
                    }
                    if (group.Options.Count > 0) {
                        string groupPath = Path.Combine(modPath, $"group_" + (1 + i++).ToString()
                        .PadLeft(3, '0') + $"_{group.Name.ToLower().Replace(" ", "_")}.json");
                        ExportGroup(groupPath, group);
                    }
                }
                while (_exportCompletion < _exportMax) {
                    Thread.Sleep(500);
                }
                foreach (TextureSet textureSet in textureSetList) {
                    textureSet.CleanTempFiles();
                }
            } catch (Exception e) {
                OnError?.Invoke(this, e.Message);
            }
        }

        private string GetDiskPath(string internalPath, string modPath, string id) {
            return !string.IsNullOrEmpty(internalPath) ?
            Path.Combine(modPath, AppendIdentifier(ImageManipulation.AddSuffix(
            RedirectToDisk(internalPath), "_" + id))) : "";
        }

        private string GetHashFromTextureSet(TextureSet textureSet) {
            string backupHash = "";
            if (textureSet.BackupTexturePaths != null) {
                if (!textureSet.BackupTexturePaths.IsFace) {
                    backupHash = (RaceInfo.ReverseRaceLookup(textureSet.InternalBasePath) == 6 ?
                    textureSet.BackupTexturePaths.BaseSecondary : textureSet.BackupTexturePaths.Base).GetHashCode().ToString();
                } else {
                    backupHash = (textureSet.BackupTexturePaths.Base + textureSet.BackupTexturePaths.BaseSecondary).GetHashCode().ToString();
                }
            }
            return (textureSet.FinalBase.GetHashCode().ToString() +
                textureSet.GroupName.GetHashCode().ToString() +
                textureSet.FinalNormal.GetHashCode().ToString() +
                textureSet.FinalMask.GetHashCode().ToString() +
                textureSet.Glow.GetHashCode().ToString() +
                textureSet.Material.GetHashCode().ToString() + backupHash).GetHashCode().ToString();
        }

        public string RedirectToDisk(string path) {
            return @"do_not_edit\textures\" + Path.GetFileName(path.Replace("/", @"\"));
        }
        public void AddDetailedGroupOption(string path, string diskPath, string name, string alternateName,
            TextureSet textureSet, List<TextureSet> textureSets, Group group, Option inputOption, out Option outputOption) {
            if (!textureSet.IsChildSet) {
                outputOption = new Option((textureSets.Count > 1 ? textureSet.TextureSetName + " " : "")
                + name + (textureSet.ChildSets.Count > 0 ? " (Universal)" : ""), 0);
                group.Options.Add(outputOption);
            } else {
                outputOption = inputOption;
            }
            outputOption.Files.Add(path, diskPath);
        }
        private bool MaskLogic(TextureSet textureSet, string maskDiskPath, bool skipTexExport) {
            bool outputGenerated = false;
            if (!string.IsNullOrEmpty(textureSet.FinalMask) && !string.IsNullOrEmpty(textureSet.InternalMaskPath)) {
                if (!string.IsNullOrEmpty(textureSet.FinalBase) && !textureSet.InternalMaskPath.Contains("/eye/")
                    && (textureSet.InternalMaskPath.Contains("obj/face") || textureSet.InternalMaskPath.Contains("obj/body"))) {
                    if (!skipTexExport) {
                        Task.Run(() => ExportTex(textureSet.FinalMask, maskDiskPath, ExportType.DTMask, "", textureSet.FinalBase));
                    }
                } else if (textureSet.InternalMaskPath.Contains("etc_") || textureSet.InternalMaskPath.Contains("hair")) {
                    if (!skipTexExport) {
                        Task.Run(() => ExportTex(textureSet.FinalMask, maskDiskPath, ExportType.DontManipulate));
                    }
                } else {
                    if (!skipTexExport) {
                        Task.Run(() => ExportTex(textureSet.FinalMask, maskDiskPath, ExportType.None));
                    }
                }
                outputGenerated = true;
            } else if (!string.IsNullOrEmpty(textureSet.FinalBase) && !string.IsNullOrEmpty(textureSet.InternalMaskPath)
                      && _generateMulti && !(textureSet.InternalMaskPath.ToLower().Contains("iri"))) {
                if (!textureSet.IgnoreMaskGeneration) {
                    if (!skipTexExport) {
                        Task.Run(() => ExportTex(textureSet.FinalBase, maskDiskPath, ExportType.Mask, "",
                        textureSet.FinalBase, textureSet.BackupTexturePaths != null ? textureSet.BackupTexturePaths.Base : ""));
                    }
                    outputGenerated = true;
                }
            }
            if (skipTexExport) {
                OnProgressChange?.Invoke(this, EventArgs.Empty);
            }
            return outputGenerated;
        }
        private bool MaterialLogic(TextureSet textureSet, string materialDiskPath, bool skipMaterialExport) {
            bool outputGenerated = false;
            if ((!string.IsNullOrEmpty(textureSet.Material)
                && !string.IsNullOrEmpty(textureSet.InternalMaterialPath))
                || !string.IsNullOrEmpty(textureSet.Glow)) {
                if (!skipMaterialExport) {
                    if (!_mtrlCache.ContainsKey(materialDiskPath)) {
                        _mtrlCache[materialDiskPath] = textureSet;
                        Task.Run(() => {
                            try {
                                Directory.CreateDirectory(Path.GetDirectoryName(materialDiskPath));
                                string value = !string.IsNullOrEmpty(textureSet.Material) ?
                                textureSet.Material :
                                Path.Combine((!string.IsNullOrEmpty(BasePath) ? BasePath :
                                GlobalPathStorage.OriginalBaseDirectory),
                                textureSet.InternalBasePath.Contains("eye") ?
                                @"res\materials\eye_glow.mtrl"
                                : @"res\materials\skin_glow.mtrl");

                                // Read donor .mtrl file
                                var data = File.ReadAllBytes(value);
                                MtrlFile mtrlFile = new MtrlFile(data);
                                int index = 0;

                                // Set texture paths on material.
                                if (!string.IsNullOrEmpty(textureSet.InternalBasePath)) {
                                    mtrlFile.Textures[index++].Path = textureSet.InternalBasePath;
                                }
                                mtrlFile.Textures[index++].Path = textureSet.InternalNormalPath;
                                mtrlFile.Textures[index++].Path = textureSet.InternalMaskPath;

                                if (!string.IsNullOrEmpty(textureSet.Glow)) {
                                    // Get emmisive values
                                    MtrlFile.Constant constant = new MtrlFile.Constant();
                                    foreach (var item in mtrlFile.ShaderPackage.Constants) {
                                        if (item.Id == 0x38A64362) {
                                            Color colour = ImageManipulation.CalculateMajorityColour(GetMergedBitmap(textureSet.Glow));
                                            constant = item;
                                            var constantValue = mtrlFile.GetConstantValue<float>(constant);

                                            // Set emmisive colour RGB
                                            constantValue[0] = (float)colour.R / 255f;
                                            constantValue[1] = (float)colour.G / 255f;
                                            constantValue[2] = (float)colour.B / 255f;
                                            break;
                                        }
                                    }
                                }
                                Stopwatch timeoutTimer = new Stopwatch();
                                timeoutTimer.Start();
                                while (TexIO.IsFileLocked(materialDiskPath) && timeoutTimer.ElapsedMilliseconds < 30000) {
                                    Thread.Sleep(1000);
                                }
                                File.WriteAllBytes(materialDiskPath, mtrlFile.Write());
                            } catch (Exception e) {
                                OnError?.Invoke(this, e.Message);
                            }
                            OnProgressChange?.Invoke(this, EventArgs.Empty);
                        });
                    } else {
                        OnProgressChange?.Invoke(this, EventArgs.Empty);
                    }
                    outputGenerated = true;
                }
            }
            if (skipMaterialExport) {
                OnProgressChange?.Invoke(this, EventArgs.Empty);
            }
            return outputGenerated;
        }

        private bool NormalLogic(TextureSet textureSet, string normalDiskPath, bool skipTexExport) {
            bool outputGenerated = false;
            if (!string.IsNullOrEmpty(textureSet.FinalNormal) && !string.IsNullOrEmpty(textureSet.InternalNormalPath)) {
                if (_generateNormals && !textureSet.IgnoreNormalGeneration && !string.IsNullOrEmpty(textureSet.FinalBase)) {
                    if (!skipTexExport) {
                        Task.Run(() => ExportTex(textureSet.FinalNormal, normalDiskPath, ExportType.MergeNormal,
                        textureSet.FinalBase, textureSet.NormalMask,
                        textureSet.BackupTexturePaths != null ? textureSet.BackupTexturePaths.Normal : "", textureSet.NormalCorrection, !textureSet.InternalBasePath.Contains("eye") ? textureSet.Glow : ""));
                    }
                    outputGenerated = true;
                } else {
                    if (!skipTexExport) {
                        Task.Run(() => ExportTex(textureSet.FinalNormal, normalDiskPath, ExportType.None, "", "",
                    textureSet.BackupTexturePaths != null ? textureSet.BackupTexturePaths.Normal : "", "", !textureSet.InternalBasePath.Contains("eye") ? textureSet.Glow : "",
                    false, textureSet.InvertNormalAlpha || !string.IsNullOrEmpty(textureSet.Glow), !string.IsNullOrEmpty(textureSet.Glow), blackOutTransparentRgb: true));
                    }
                    outputGenerated = true;
                }
            } else if ((!string.IsNullOrEmpty(textureSet.FinalBase) || !string.IsNullOrEmpty(textureSet.Glow))
                  && !string.IsNullOrEmpty(textureSet.InternalNormalPath) && _generateNormals) {
                if (!textureSet.IgnoreNormalGeneration) {
                    if (textureSet.BackupTexturePaths != null) {
                        if (!skipTexExport) {
                            Task.Run(() => ExportTex((Path.Combine(_basePath, textureSet.BackupTexturePaths.Normal)),
                            normalDiskPath, ExportType.MergeNormal, textureSet.FinalBase, textureSet.NormalMask,
                            (textureSet.BackupTexturePaths != null ? textureSet.BackupTexturePaths.Normal : ""),
                            textureSet.NormalCorrection, !textureSet.InternalBasePath.Contains("eye") ? textureSet.Glow : "", textureSet.InvertNormalGeneration));
                        }
                        outputGenerated = true;
                    } else {
                        if (!textureSet.InternalBasePath.Contains("eye")) {
                            if (!skipTexExport) {
                                Task.Run(() => ExportTex(textureSet.FinalBase, normalDiskPath,
                                ExportType.Normal, "", textureSet.NormalMask, textureSet.BackupTexturePaths != null ?
                                textureSet.BackupTexturePaths.Base : "",
                                textureSet.NormalCorrection, textureSet.Glow, textureSet.InvertNormalGeneration));
                            }
                        }
                        outputGenerated = true;
                    }
                }
            } else if (!string.IsNullOrEmpty(textureSet.Glow)
                  && !string.IsNullOrEmpty(textureSet.InternalNormalPath)) {
                if (!textureSet.InternalBasePath.Contains("eye")) {
                    if (!skipTexExport) {
                        Task.Run(() => ExportTex(textureSet.BackupTexturePaths != null ?
                        textureSet.BackupTexturePaths.Normal : "", normalDiskPath,
                        ExportType.None, "", textureSet.NormalMask, "",
                        textureSet.NormalCorrection, textureSet.Glow, textureSet.InvertNormalGeneration, textureSet.InternalBasePath.Contains("fac_"), blackOutTransparentRgb: true));
                    }
                }
                outputGenerated = true;
            }
            if (skipTexExport) {
                OnProgressChange?.Invoke(this, EventArgs.Empty);
            }
            return outputGenerated;
        }

        private bool BaseLogic(TextureSet textureSet, string baseTextureDiskPath, bool skipTexExport) {
            bool outputGenerated = false;
            string underlay = "";
            if (textureSet.BackupTexturePaths != null) {
                if (!textureSet.BackupTexturePaths.IsFace) {
                    underlay = (RaceInfo.ReverseRaceLookup(textureSet.InternalBasePath) == 6 ?
                         textureSet.BackupTexturePaths.BaseSecondary : textureSet.BackupTexturePaths.Base);
                } else {
                    underlay = textureSet.BackupTexturePaths.Base;
                }
            }
            if (!string.IsNullOrEmpty(textureSet.FinalBase)) {
                if (!skipTexExport) {
                    Task.Run(() => ExportTex(textureSet.FinalBase, baseTextureDiskPath, ExportType.None, "", "", underlay));
                }
                outputGenerated = true;
            }
            if (skipTexExport) {
                OnProgressChange?.Invoke(this, EventArgs.Empty);
            }
            return outputGenerated;
        }

        public void CleanGeneratedAssets(string path) {
            foreach (string file in Directory.EnumerateFiles(path)) {
                if (file.Contains("_generated")) {
                    File.Delete(file);
                }
                if (file.EndsWith(".json")) {
                    bool isGenerated = false;
                    using (StreamReader jsonFile = File.OpenText(file)) {
                        try {
                            JsonSerializer serializer = new JsonSerializer();
                            Group group = (Group)serializer.Deserialize(jsonFile, typeof(Group));
                            if (!string.IsNullOrEmpty(group.Description) && group.Description.Contains("-generated")) {
                                isGenerated = true;
                            }
                        } catch {
                            // Todo: should we report when we skip a .json we cant read?
                        }
                    }
                    if (isGenerated) {
                        File.Delete(file);
                    }
                }
            }
            foreach (string directory in Directory.EnumerateDirectories(path)) {
                CleanGeneratedAssets(directory);
            }
        }

        private void ExportGroup(string path, Group group) {
            group.Description += " -generated";
            bool isSingle = group.Type == "Single";
            if (path != null) {
                if (group.Options.Count > (isSingle ? int.MaxValue : 32)) {
                    int groupsToSplitTo = group.Options.Count / 32;
                    for (int i = 0; i < groupsToSplitTo; i++) {
                        int rangeStartingPoint = 32 * i;
                        int maxRange = group.Options.Count - rangeStartingPoint;
                        Group newGroup = new Group(group.Name + $" ({i + 1})", group.Description + " -generated",
                                        group.Priority, group.Type, group.DefaultSettings);
                        newGroup.Options = group.Options.GetRange(rangeStartingPoint, maxRange > 32 ? 32 : maxRange);
                        using (StreamWriter file = File.CreateText(path.Replace(".", $" ({i})."))) {
                            JsonSerializer serializer = new JsonSerializer();
                            serializer.Formatting = Formatting.Indented;
                            serializer.Serialize(file, newGroup);
                        }
                    }
                } else if (group.Options.Count > 0) {
                    using (StreamWriter file = File.CreateText(path)) {
                        JsonSerializer serializer = new JsonSerializer();
                        serializer.Formatting = Formatting.Indented;
                        serializer.Serialize(file, group);
                    }
                }
            }
        }

        public enum ExportType {
            None,
            Normal,
            Mask,
            MergeNormal,
            Glow,
            GlowEyeMask,
            XNormalImport,
            DontManipulate,
            DTMask,
        }
        public async Task<bool> ExportTex(string inputFile, string outputFile, ExportType exportType = ExportType.None,
            string baseTextureNormal = "", string modifierMap = "", string layeringImage = "",
            string normalCorrection = "", string alphaOverride = "", bool modifier = false, bool invertAlpha = false, bool dontInvertAlphaOverride = false, bool blackOutTransparentRgb = false) {
            byte[] data = new byte[0];
            bool skipPngTexConversion = false;
            try {
                using (MemoryStream stream = new MemoryStream()) {
                    switch (exportType) {
                        case ExportType.None:
                            ExportTypeNone(inputFile, layeringImage, stream, alphaOverride, invertAlpha, dontInvertAlphaOverride, blackOutTransparentRgb);
                            break;
                        case ExportType.DontManipulate:
                            data = TexIO.GetTexBytes(inputFile);
                            skipPngTexConversion = true;
                            break;
                        case ExportType.Glow:
                            ExportTypeGlow(inputFile, modifierMap, layeringImage, stream);
                            break;
                        case ExportType.GlowEyeMask:
                            ExportTypeGlowEyeMask(inputFile, modifierMap, stream);
                            break;
                        case ExportType.DTMask:
                            ExportTypeDTMask(inputFile, modifierMap, stream);
                            break;
                        case ExportType.Normal:
                            ExportTypeNormal(inputFile, outputFile, modifierMap, normalCorrection, modifier, stream, alphaOverride, invertAlpha);
                            break;
                        case ExportType.Mask:
                            ExportTypeMask(inputFile, layeringImage, exportType, modifierMap, stream);
                            break;
                        case ExportType.MergeNormal:
                            ExportTypeMergeNormal(inputFile, outputFile, layeringImage, baseTextureNormal, modifierMap,
                            normalCorrection, stream, modifier, alphaOverride, invertAlpha);
                            break;
                        case ExportType.XNormalImport:
                            ExportTypeXNormalImport(inputFile, baseTextureNormal, stream);
                            break;
                    }
                    if (!skipPngTexConversion) {
                        stream.Flush();
                        stream.Position = 0;
                        if (stream.Length > 0) {
                            PenumbraTextureImporter.PngToTex(stream, out data);
                            stream.Position = 0;
                        }
                    }
                }
                if (data.Length > 0) {
                    Directory.CreateDirectory(Path.GetDirectoryName(outputFile));
                    while (TexIO.IsFileLocked(outputFile)) {
                        Thread.Sleep(500);
                    }
                    if (File.Exists(outputFile)) {
                        File.Delete(outputFile);
                    }
                    File.WriteAllBytes(outputFile, data);
                }
            } catch (Exception e) {
                OnError?.Invoke(this, e.Message);
            }
            if (OnProgressChange != null) {
                OnProgressChange.Invoke(this, EventArgs.Empty);
            }
            return true;
        }

        private void ExportTypeXNormalImport(string inputFile, string baseTextureNormal, Stream stream) {
            using (Bitmap bitmap = TexIO.ResolveBitmap(inputFile)) {
                if (bitmap != null) {
                    Bitmap underlay = new Bitmap(bitmap.Width, bitmap.Height, PixelFormat.Format32bppArgb);
                    Graphics g = Graphics.FromImage(underlay);
                    g.Clear(Color.FromArgb(255, 160, 113, 94));
                    if (!string.IsNullOrEmpty(baseTextureNormal)) {
                        g.DrawImage(TexIO.ResolveBitmap(baseTextureNormal), 0, 0, bitmap.Width, bitmap.Height);
                    }
                    MapWriting.TransplantData(underlay, bitmap).Save(stream, ImageFormat.Png);
                }
            }
        }

        private void ExportTypeMergeNormal(string inputFile, string outputFile, string layeringImage,
            string baseTextureNormal, string modifierMap, string normalCorrection, Stream stream, bool modifier, string alphaOverride, bool invertAlpha) {
            Bitmap output = null;
            if (!string.IsNullOrEmpty(baseTextureNormal)) {
                lock (_normalCache) {
                    if (!_normalCache.ContainsKey(baseTextureNormal)) {
                        using (Bitmap baseTexture = TexIO.ResolveBitmap(baseTextureNormal)) {
                            if (baseTexture != null) {
                                using (Bitmap canvasImage = new Bitmap(baseTexture.Size.Width,
                                    baseTexture.Size.Height, PixelFormat.Format32bppArgb)) {
                                    output = null;
                                    if (File.Exists(modifierMap)) {
                                        using (Bitmap normalMaskBitmap = TexIO.ResolveBitmap(modifierMap)) {
                                            output = ImageManipulation.MergeNormals(TexIO.ResolveBitmap(inputFile), baseTexture,
                                                canvasImage, normalMaskBitmap, baseTextureNormal, modifier);
                                        }
                                    } else {
                                        using (Bitmap bitmap = TexIO.ResolveBitmap(inputFile)) {
                                            if (bitmap != null) {
                                                if (!string.IsNullOrEmpty(layeringImage)) {
                                                    Bitmap bottomLayer = TexIO.ResolveBitmap(Path.Combine(_basePath, layeringImage));
                                                    Bitmap topLayer = GetMergedBitmap(inputFile);
                                                    output = ImageManipulation.MergeNormals(ImageManipulation.LayerImages(bottomLayer, topLayer), baseTexture, canvasImage, null, baseTextureNormal, modifier);
                                                } else {
                                                    output = ImageManipulation.MergeNormals(TexIO.ResolveBitmap(inputFile), baseTexture, canvasImage, null, baseTextureNormal, modifier);
                                                }
                                            }
                                        }
                                    }
                                    if (!string.IsNullOrEmpty(normalCorrection)) {
                                        output = ImageManipulation.ResizeAndMerge(output, TexIO.ResolveBitmap(normalCorrection));
                                    }
                                    if (!string.IsNullOrEmpty(alphaOverride)) {
                                        var bitmap = Grayscale.MakeGrayscale(TexIO.ResolveBitmap(alphaOverride));
                                        var rgb = ImageManipulation.ExtractRGB(output);
                                        if (output.Size.Height < bitmap.Size.Height) {
                                            rgb = ImageManipulation.Resize(rgb, bitmap.Size.Width, bitmap.Size.Height);
                                        } else {
                                            bitmap = ImageManipulation.Resize(bitmap, output.Size.Width, output.Size.Height);
                                        }
                                        output = ImageManipulation.MergeAlphaToRGB(bitmap, rgb);
                                    }
                                    output.Save(stream, ImageFormat.Png);
                                    _normalCache.Add(baseTextureNormal, output);
                                }
                            }
                        }
                    } else {
                        _normalCache[baseTextureNormal].Save(stream, ImageFormat.Png);
                    }
                }
            }
        }

        private void ExportTypeMask(string inputFile, string layeringImage, ExportType exportType, string modifierMap, Stream stream) {
            lock (_maskCache) {
                if (_maskCache.ContainsKey(inputFile)) {
                    TexIO.SaveBitmap(_maskCache[inputFile], stream);
                } else {
                    using (Bitmap bitmap = TexIO.ResolveBitmap(inputFile)) {
                        if (bitmap != null) {
                            Bitmap image;
                            if (layeringImage != null) {
                                image = new Bitmap(bitmap.Width, bitmap.Height, PixelFormat.Format32bppArgb);
                                Bitmap layer = TexIO.ResolveBitmap(Path.Combine(_basePath,
                                    layeringImage));
                                Graphics g = Graphics.FromImage(image);
                                g.Clear(Color.Transparent);
                                g.DrawImage(layer, 0, 0, bitmap.Width, bitmap.Height);
                                g.DrawImage(GetMergedBitmap(inputFile), 0, 0, bitmap.Width, bitmap.Height);
                            } else {
                                image = bitmap;
                            }
                            Bitmap generatedMulti = ImageManipulation.ConvertBaseToDawntrailSkinMulti(image);
                            Bitmap mask = !string.IsNullOrEmpty(modifierMap)
                                ? MapWriting.CalculateMulti(generatedMulti, TexIO.ResolveBitmap(modifierMap))
                                : generatedMulti;
                            mask.Save(stream, ImageFormat.Png);
                            _maskCache.Add(inputFile, mask);
                        }
                    }
                }
            }
        }

        private void ExportTypeNormal(string inputFile, string outputFile, string modifierMap,
            string normalCorrection, bool modifier, Stream stream, string alphaOverride, bool invertAlpha) {
            Bitmap output;
            lock (_normalCache) {
                if (_normalCache.ContainsKey(inputFile)) {
                    output = _normalCache[inputFile];
                } else {
                    using (Bitmap bitmap = TexIO.ResolveBitmap(inputFile)) {
                        using (Bitmap target = new Bitmap(bitmap.Size.Width, bitmap.Size.Height, PixelFormat.Format32bppArgb)) {
                            Graphics g = Graphics.FromImage(target);
                            g.Clear(Color.Transparent);
                            ImageManipulation.DrawImage(target, bitmap, 0, 0, bitmap.Width, bitmap.Height);
                            if (File.Exists(modifierMap)) {
                                using (Bitmap normalMaskBitmap = TexIO.ResolveBitmap(modifierMap)) {
                                    output = Normal.Calculate(modifier ? ImageManipulation.InvertImage(target)
                                        : target, normalMaskBitmap);
                                }
                            } else {
                                output = Normal.Calculate(modifier ? ImageManipulation.InvertImage(target) : target);
                            }
                            if (!string.IsNullOrEmpty(alphaOverride)) {
                                output = ImageManipulation.LayerImages(output, output, alphaOverride, invertAlpha);
                            }
                            _normalCache.Add(inputFile, output);
                        }
                    }
                }
            }
            if (!string.IsNullOrEmpty(normalCorrection)) {
                output = ImageManipulation.ResizeAndMerge(output, TexIO.ResolveBitmap(normalCorrection));
            }
            output.Save(stream, ImageFormat.Png);
        }

        private void ExportTypeDTMask(string inputFile, string mask, Stream stream) {
            string descriminator = inputFile + mask + "glowMulti";
            Bitmap glowOutput;
            lock (_glowCache) {
                if (_glowCache.ContainsKey(descriminator)) {
                    glowOutput = _glowCache[descriminator];
                    glowOutput.Save(stream, ImageFormat.Png);
                } else {
                    using (Bitmap bitmap = TexIO.ResolveBitmap(inputFile)) {
                        if (bitmap != null) {
                            Bitmap maskChannelMap = MapWriting.CalculateMulti(bitmap, TexIO.ResolveBitmap(mask));
                            maskChannelMap.Save(stream, ImageFormat.Png);
                            _glowCache.Add(descriminator, maskChannelMap);
                        }
                    }
                }
            }
        }

        private void ExportTypeGlowEyeMask(string inputFile, string mask, Stream stream) {
            string descriminator = inputFile + mask + "glowEyeMulti";
            Bitmap glowOutput;
            lock (_glowCache) {
                if (_glowCache.ContainsKey(descriminator)) {
                    glowOutput = _glowCache[descriminator];
                    glowOutput.Save(stream, ImageFormat.Png);
                } else {
                    using (Bitmap bitmap = TexIO.ResolveBitmap(inputFile)) {
                        if (bitmap != null) {
                            Bitmap glowBitmap = MapWriting.CalculateEyeMulti(bitmap, TexIO.ResolveBitmap(mask));
                            glowBitmap.Save(stream, ImageFormat.Png);
                            _glowCache.Add(descriminator, glowBitmap);
                        }
                    }
                }
            }
        }

        private void ExportTypeGlow(string inputFile, string glowMap, string layeringImage, Stream stream) {
            Bitmap glowOutput = null;
            string descriminator = inputFile + glowMap + "glow";
            lock (_glowCache) {
                if (_glowCache.ContainsKey(descriminator)) {
                    glowOutput = _glowCache[descriminator];
                    glowOutput.Save(stream, ImageFormat.Png);
                } else {
                    using (Bitmap bitmap = TexIO.ResolveBitmap(inputFile)) {
                        if (bitmap != null) {
                            if (!string.IsNullOrEmpty(layeringImage)) {
                                Bitmap image = new Bitmap(bitmap.Width, bitmap.Height, PixelFormat.Format32bppArgb);
                                Bitmap layer = TexIO.ResolveBitmap(Path.Combine(_basePath,
                                    layeringImage));
                                Graphics g = Graphics.FromImage(image);
                                g.Clear(Color.Transparent);
                                //g.CompositingQuality = CompositingQuality.HighQuality;
                                //g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                                //g.SmoothingMode = SmoothingMode.HighQuality;
                                g.DrawImage(layer, 0, 0, bitmap.Width, bitmap.Height);
                                g.DrawImage(GetMergedBitmap(inputFile), 0, 0, bitmap.Width, bitmap.Height);
                                Bitmap glowBitmap = MapWriting.CalculateBase(image,
                                    ImageManipulation.Resize(GetMergedBitmap(glowMap), bitmap.Width, bitmap.Height));
                                glowBitmap.Save(stream, ImageFormat.Png);
                                _glowCache.Add(descriminator, glowBitmap);
                            } else {
                                Bitmap glowBitmap = MapWriting.CalculateBase(bitmap, TexIO.ResolveBitmap(glowMap));
                                glowBitmap.Save(stream, ImageFormat.Png);
                                _glowCache.Add(descriminator, glowBitmap);
                            }
                        }
                    }
                }
            }
        }

        private void ExportTypeNone(string inputFile, string layeringImage, Stream stream, string alphaOverride = "", bool invertAlpha = false, bool dontInvertAlphaOverrid = false, bool blackOutTransparentRgb = false) {
            if (!string.IsNullOrEmpty(layeringImage)) {
                Bitmap bottomLayer = TexIO.ResolveBitmap(Path.Combine(_basePath, layeringImage));
                Bitmap topLayer = GetMergedBitmap(inputFile);
                var layered = ImageManipulation.LayerImages(bottomLayer, topLayer, alphaOverride, invertAlpha, dontInvertAlphaOverrid);
                if (blackOutTransparentRgb) {
                    // Prefer dragged texture alpha when it is actually populated.
                    // Some DDS normals do not carry meaningful alpha and should keep layered/backup alpha.
                    using (Bitmap topAlpha = ImageManipulation.ExtractAlpha(topLayer)) {
                        using (Bitmap layeredAlpha = ImageManipulation.ExtractAlpha(layered)) {
                            Bitmap selectedAlpha = ImageManipulation.HasUsableAlpha(topAlpha, 16, 0.05f) ? topAlpha : layeredAlpha;
                            // Keep dragged normal RGB intact; only replace alpha mask source.
                                    using (Bitmap topRgb = ImageManipulation.ExtractRGB(topLayer)) {
                                        using (Bitmap preservedAlpha = ImageManipulation.MergeAlphaToRGB(selectedAlpha, topRgb)) {
                                            using (Bitmap blacked = ImageManipulation.BlackoutTransparentRgb(preservedAlpha, 2)) {
                                                TexIO.SaveBitmap(blacked, stream);
                                            }
                                        }
                                    }
                        }
                    }
                } else {
                    TexIO.SaveBitmap(layered, stream);
                }
            } else {
                using (Bitmap bitmap = GetMergedBitmap(inputFile.StartsWith(@"res\") ? Path.Combine(_basePath, inputFile) : inputFile)) {
                    if (bitmap != null) {
                        if (string.IsNullOrEmpty(alphaOverride)) {
                            if (blackOutTransparentRgb) {
                                using (Bitmap blacked = ImageManipulation.BlackoutTransparentRgb(bitmap)) {
                                    TexIO.SaveBitmap(blacked, stream);
                                }
                            } else {
                                TexIO.SaveBitmap(bitmap, stream);
                            }
                        } else {
                            using (Bitmap merged = ImageManipulation.MergeAlphaToRGB(TexIO.Resize(Grayscale.MakeGrayscale(TexIO.ResolveBitmap(alphaOverride)), bitmap.Width, bitmap.Height), bitmap)) {
                                if (blackOutTransparentRgb) {
                                    using (Bitmap blacked = ImageManipulation.BlackoutTransparentRgb(merged)) {
                                        TexIO.SaveBitmap(blacked, stream);
                                    }
                                } else {
                                    TexIO.SaveBitmap(merged, stream);
                                }
                            }
                        }
                    }
                }
            }
        }

        public string AppendIdentifier(string value) {
            return ImageManipulation.AddSuffix(value, "_generated");
        }
    }
}
