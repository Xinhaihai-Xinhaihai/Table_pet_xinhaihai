namespace XinHaiHai;

public enum Rarity { Common, Rare, Epic, Legendary }

public class Gift
{
    public string Key;
    public string Icon;         // emoji 图标
    public string Name;
    public Rarity Rarity;
    public int Price;
    public double LockHours;    // >0:送后保持满快乐值的小时数;=0:只加快乐值
    public double HappyAdd;     // 即时增加的快乐值(0~1000 量纲)
    public string Line;         // 收到礼物时她说的话

    public string RarityText => Rarity switch
    {
        Rarity.Common => "普通",
        Rarity.Rare => "稀有",
        Rarity.Epic => "史诗",
        _ => "传说"
    };

    public string RarityColor => Rarity switch
    {
        Rarity.Common => "#9AA7B0",
        Rarity.Rare => "#3B9AE1",
        Rarity.Epic => "#B061D6",
        _ => "#E8A017"
    };
}

/// <summary>礼物商店:16 件礼物,价格随稀有度上涨,部分礼物可锁定满快乐值一段时间。</summary>
public static class Shop
{
    public static readonly List<Gift> Gifts = new()
    {
        // —— 普通(便宜,只加快乐值) ——
        new(){ Key="candy",   Icon="🍬", Name="水果糖",     Rarity=Rarity.Common,    Price=60,   HappyAdd=180,  Line="糖果!嘿嘿,甜甜的,谢谢主人~" },
        new(){ Key="flower",  Icon="🌸", Name="樱花簪",     Rarity=Rarity.Common,    Price=90,   HappyAdd=260,  Line="发簪!好精致呀,本座戴上给你看~" },
        new(){ Key="plush",   Icon="🧸", Name="小熊玩偶",   Rarity=Rarity.Common,    Price=130,  HappyAdd=340,  Line="软软的玩偶!晚上抱着它睡觉,嘿嘿~" },
        new(){ Key="ribbon",  Icon="🎀", Name="红发带",     Rarity=Rarity.Common,    Price=160,  HappyAdd=420,  Line="新的发带!好看吗?本座换上给你看~" },

        // —— 稀有(中价,加满或小锁) ——
        new(){ Key="book",    Icon="📖", Name="绘本故事",   Rarity=Rarity.Rare,      Price=240,  HappyAdd=650,  Line="给本座讲故事吗?坐好坐好,吾要听!" },
        new(){ Key="umbrella",Icon="🌂", Name="和纸伞",     Rarity=Rarity.Rare,      Price=320,  HappyAdd=1000, Line="哇——好漂亮的伞!下雨天一起撑好不好?" },
        new(){ Key="koi",     Icon="🎏", Name="鲤鱼旗",     Rarity=Rarity.Rare,      Price=420,  LockHours=0.5, HappyAdd=1000, Line="鲤鱼旗!挂起来,今天一整个下午都好开心!" },
        new(){ Key="lantern", Icon="🏮", Name="祭典灯笼",   Rarity=Rarity.Rare,      Price=560,  LockHours=1,   HappyAdd=1000, Line="是庙会的灯笼!牵着手去逛祭典吧,主人!" },

        // —— 史诗(高价,较长锁) ——
        new(){ Key="kimono",  Icon="👘", Name="樱花振袖",   Rarity=Rarity.Epic,      Price=850,  LockHours=2,   HappyAdd=1000, Line="这么华丽的振袖……本座、本座会一直开心的!" },
        new(){ Key="cake",    Icon="🍰", Name="豪华蛋糕",   Rarity=Rarity.Epic,      Price=1100, LockHours=3,   HappyAdd=1000, Line="哇啊!整个蛋糕都是吾的?最幸福的一天!" },
        new(){ Key="fireworks",Icon="🎆",Name="夏夜烟花",   Rarity=Rarity.Epic,      Price=1500, LockHours=4,   HappyAdd=1000, Line="烟花——!和主人一起看烟花,吾会记一辈子的!" },
        new(){ Key="music_box",Icon="🎶",Name="八音盒",     Rarity=Rarity.Epic,      Price=2000, LockHours=6,   HappyAdd=1000, Line="叮铃铃……好温柔的曲子,吾要单曲循环一整天!" },

        // —— 传说(极贵,超长锁) ——
        new(){ Key="sword",   Icon="🗡️", Name="妖刀·村雨",  Rarity=Rarity.Legendary, Price=3200, LockHours=12,  HappyAdd=1000, Line="这……这是吾的本体!主人竟为吾寻回了它,吾…吾好感动!" },
        new(){ Key="crown",   Icon="👑", Name="花之王冠",   Rarity=Rarity.Legendary, Price=4800, LockHours=24,  HappyAdd=1000, Line="王冠?本座今天就是主人一个人的公主啦!嘿嘿~" },
        new(){ Key="ring",    Icon="💍", Name="约定戒指",   Rarity=Rarity.Legendary, Price=7500, LockHours=72,  HappyAdd=1000, Line="和主人的约定之戒……本座这一生,都陪定你了。" },
        new(){ Key="star",    Icon="🌟", Name="落星之愿",   Rarity=Rarity.Legendary, Price=12000,LockHours=168, HappyAdd=1000, Line="接住了主人许愿的流星……接下来整整一周,吾都是全世界最幸福的存在!" },
    };

    public static Gift Find(string key) => Gifts.FirstOrDefault(g => g.Key == key);
}
