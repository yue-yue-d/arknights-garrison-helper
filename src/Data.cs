using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;

namespace WeishuHelper
{
    /// <summary>程序根目录（含「数据」文件夹的那一层），从 exe 所在目录往上找。</summary>
    public static class Paths
    {
        public static readonly string Root = FindRoot();
        public static string Data => Path.Combine(Root, "数据");
        public static string Guides => Path.Combine(Root, "攻略");
        public static string MapGuides => Path.Combine(Root, "攻略", "地图");
        public static string Plans => Path.Combine(Root, "布阵");
        public static string SettingsFile => Path.Combine(Root, "设置.json");
        public static string Tools => Path.Combine(Root, "工具");

        static string FindRoot()
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d != null)
            {
                if (Directory.Exists(Path.Combine(d.FullName, "数据"))) return d.FullName;
                d = d.Parent;
            }
            return AppContext.BaseDirectory;
        }
    }

    public static class Json
    {
        public static readonly JsonSerializerOptions Opt = new()
        {
            WriteIndented = true,
            IncludeFields = true,
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        public static T Read<T>(string path) where T : new()
        {
            try
            {
                if (File.Exists(path))
                    return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Opt) ?? new T();
            }
            catch (Exception e) { App.Log($"读取失败 {path}: {e.Message}"); }
            return new T();
        }

        public static void Write<T>(string path, T value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(value, Opt));
            File.Move(tmp, path, true);
        }
    }

    // ---------- 规程数据（由 工具/更新数据.py 生成） ----------

    public class Operator
    {
        public string 名称 = "";
        public int 等阶;
        public string 原型 = "";
        public List<string> 盟约 = new();
        public string 特质类型 = "";
        public string 特质 = "";
        public string 精锐特质 = "";
        public string 职业 = "";
        public string 分支 = "";
        public string 位置 = "";
        public string 图片 = "";
        // 支援（甄选）干员才有的：类别 = 六星 / 精英干员，阵营 = PRTS 所属国家
        public string 类别 = "", 阵营 = "";
        [JsonIgnore] public bool 支援 => 类别 != "";
        [JsonIgnore] public bool 近战 => 位置.Contains("近战") && !位置.Contains("远程");
        [JsonIgnore] public string 等阶字 => Tier.Roman(等阶);
    }

    public class Bond
    {
        public string 名称 = "";
        public string 类型 = "";
        public int 激活人数;
        public int? 强化人数;
        public bool 整备区计入;
        public string 效果 = "";
        public List<string> 干员 = new();
        public string 图片 = "";
        [JsonIgnore] public bool 核心 => 类型 == "核心";
    }

    public class Strategy
    {
        public string 策略 = "", 发起人 = "", 效果 = "", 解锁条件 = "", 图片 = "";
        public int? 血量;
    }

    public class Item
    {
        public string 名称 = "", 类型 = "", 效果 = "", 描述 = "", 图片 = "";
        public int? 等阶, 售价;
    }

    public class Phase { public string 分组 = "", 子类 = "", 名称 = "", 说明 = "", 敌人 = "", 图片 = ""; }

    public class Leader
    {
        public string 名称 = "", 图片 = "";
        public Dictionary<string, string> 血量 = new();          // 联合模拟：标准/险境/绝境/终极
        public Dictionary<string, string> 个人模拟血量 = new();  // 极速模拟、规程等级1～20
    }

    public class MapInfo
    {
        public string 编号 = "";
        public List<string> 说明 = new(), 装置 = new(), 地形 = new();
        public List<int> 场地 = new();
        public List<string> 图片 = new();
    }

    public class MapsBlock
    {
        public List<string> 共通 = new();
        public List<MapInfo> 战场 = new();
    }

    /// <summary>数据/网格默认.json：每个战场布阵网格的初始位置（手工量的）。</summary>
    public class GridDefault
    {
        public List<double> 角点 = new();
        public int 列 = 11, 行 = 4;
    }

    public class GridDefaults
    {
        public string 说明 = "";
        public Dictionary<string, GridDefault> 战场 = new();
    }

    public class DataFile<T> where T : new()
    {
        public Dictionary<string, string> meta = new();
        public T 列表 = new();
    }

    public static class Tier
    {
        static readonly string[] R = { "?", "Ⅰ", "Ⅱ", "Ⅲ", "Ⅳ", "Ⅴ", "Ⅵ" };
        static readonly string[] C = { "#888888", "#C5C5C5", "#FFFFFF", "#46EFBC", "#33C9DA", "#F6C60F", "#E88C1A" };
        public static string Roman(int t) => t >= 0 && t < R.Length ? R[t] : "?";
        public static string Color(int t) => t >= 0 && t < C.Length ? C[t] : C[0];
    }

    public static class GameData
    {
        public static List<Operator> Ops = new();
        public static Dictionary<string, Operator> OpBy = new();
        /// <summary>支援（甄选）干员：名单外的 6★ + 罗德岛特派高级资深干员。</summary>
        public static List<Operator> Support = new();
        /// <summary>布阵用：名单干员 + 支援干员。</summary>
        public static Dictionary<string, Operator> AnyBy = new();
        public static List<Bond> Bonds = new();
        public static Dictionary<string, Bond> BondBy = new();
        public static List<Strategy> Strategies = new();
        public static List<Item> Items = new();
        public static List<Phase> Phases = new();
        public static List<Leader> Leaders = new();
        public static MapsBlock Maps = new();
        public static Dictionary<string, GridDefault> Grids = new();
        public static string Season = "卫戍协议·规程", Updated = "";

        public static void Load()
        {
            DataFile<T> F<T>(string n) where T : new() => Json.Read<DataFile<T>>(Path.Combine(Paths.Data, n + ".json"));
            var ops = F<List<Operator>>("干员");
            Ops = ops.列表;
            if (ops.meta.TryGetValue("赛季", out var s)) Season = s;
            if (ops.meta.TryGetValue("更新时间", out var u)) Updated = u;
            OpBy = Ops.GroupBy(o => o.名称).ToDictionary(g => g.Key, g => g.First());
            Support = F<List<Operator>>("支援").列表;
            foreach (var o in Support) if (string.IsNullOrEmpty(o.类别)) o.类别 = "六星";
            AnyBy = new Dictionary<string, Operator>(OpBy);
            foreach (var o in Support) AnyBy.TryAdd(o.名称, o);
            Bonds = F<List<Bond>>("盟约").列表;
            BondBy = Bonds.ToDictionary(b => b.名称);
            Strategies = F<List<Strategy>>("策略").列表;
            Items = F<List<Item>>("装备").列表;
            Phases = F<List<Phase>>("机变").列表;
            Leaders = F<List<Leader>>("领袖").列表;
            Maps = F<MapsBlock>("地图").列表;
            Grids = Json.Read<GridDefaults>(Path.Combine(Paths.Data, "网格默认.json")).战场;
        }

        public static MapInfo Map(string id) => Maps.战场.FirstOrDefault(m => m.编号 == id);
    }

    // ---------- 设置 ----------

    public class Settings
    {
        public string 停靠 = "内侧";          // 内侧：贴在游戏画面左侧里面；外侧：贴在游戏窗口左边外面；自由：随便放
        public double 宽度 = 380;              // 逻辑像素
        public double 不透明度 = 0.9;
        public double 字号 = 13;
        public double 上边距 = 48, 下边距 = 48, 左边距 = 0;   // 停靠时相对游戏画面
        public int 自由X = 80, 自由Y = 80, 自由高 = 900;        // 物理像素
        public bool 仅游戏前台显示 = true;
        public bool 收起 = false;
        public int 收起X = -1, 收起Y = -1;
        public string 收起贴边 = "";          // 小球贴着的屏幕边：左/右/上/下，空 = 没贴边
        // 外观
        public string 主题色 = "#41DBB2";
        public string 面板色 = "#141A1F";
        public string 悬浮球色 = "";          // 空 = 跟随主题色
        public string 悬浮球图片 = "图片/通用/奖杯8.png";   // 数据/ 下的相对路径，或者完整路径
        public double 悬浮球大小 = 58;
        public bool 贴边隐藏 = true;   // 收起后小球的位置（物理像素），-1 = 还没拖过，放在悬浮窗原来的左上角
        public string 页签 = "布阵";
        public string 地图 = "#01";
        public int 场地 = 1;
        public bool 先列后行 = true;
        public string 攻略文件 = "";

        public static Settings Load() => Json.Read<Settings>(Paths.SettingsFile);
        public void Save()
        {
            try { Json.Write(Paths.SettingsFile, this); } catch (Exception e) { App.Log("保存设置失败: " + e.Message); }
        }
    }
}
