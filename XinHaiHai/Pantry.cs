namespace XinHaiHai;

public enum ConsumeKind { Food, Drink }

public class Consumable
{
    public string Key;
    public string Icon;
    public string Name;
    public ConsumeKind Kind;
    public int Price;
    public double Hunger;   // 恢复饥饿(食物为主)
    public double Thirst;   // 恢复口渴(饮料为主)
    public double Happy;    // 附带快乐值(0~1000 量纲)
    public string Line;     // 吃/喝时的台词
}

/// <summary>食堂:食物 + 饮料,各 10+ 种,与礼物商店分开。</summary>
public static class Pantry
{
    public static readonly List<Consumable> Foods = new()
    {
        new(){ Key="onigiri", Icon="🍙", Name="梅子饭团",   Kind=ConsumeKind.Food, Price=18,  Hunger=25, Happy=15, Line="饭团!梅子酸酸的,好好吃~谢谢主人!" },
        new(){ Key="dango",   Icon="🍡", Name="三色团子",   Kind=ConsumeKind.Food, Price=22,  Hunger=22, Happy=25, Line="团子最棒了!甜甜的,嘿嘿~" },
        new(){ Key="ramen",   Icon="🍜", Name="热汤拉面",   Kind=ConsumeKind.Food, Price=40,  Hunger=45, Thirst=8, Happy=20, Line="呼呼——拉面好烫,可是好满足!" },
        new(){ Key="sushi",   Icon="🍣", Name="握寿司",     Kind=ConsumeKind.Food, Price=55,  Hunger=40, Happy=30, Line="哇,寿司!主人真舍得,吾开动啦!" },
        new(){ Key="bento",   Icon="🍱", Name="幕之内便当", Kind=ConsumeKind.Food, Price=60,  Hunger=55, Happy=25, Line="满满一盒便当!营养均衡,吾会长得壮壮的!" },
        new(){ Key="taiyaki", Icon="🐟", Name="鲷鱼烧",     Kind=ConsumeKind.Food, Price=25,  Hunger=24, Happy=22, Line="鲷鱼烧的尾巴也有豆沙,幸福!" },
        new(){ Key="tempura", Icon="🍤", Name="天妇罗",     Kind=ConsumeKind.Food, Price=48,  Hunger=42, Happy=18, Line="炸虾天妇罗!脆脆的,吾最爱吃了!" },
        new(){ Key="curry",   Icon="🍛", Name="咖喱饭",     Kind=ConsumeKind.Food, Price=45,  Hunger=50, Happy=20, Line="咖喱饭!辣辣的暖到心里~" },
        new(){ Key="mochi",   Icon="🍢", Name="味噌田乐",   Kind=ConsumeKind.Food, Price=30,  Hunger=28, Happy=16, Line="烤得香香的,主人也来一串嘛~" },
        new(){ Key="peach",   Icon="🍑", Name="水蜜桃",     Kind=ConsumeKind.Food, Price=35,  Hunger=20, Thirst=15, Happy=24, Line="桃子!又多汁又甜,吾好喜欢!" },
        new(){ Key="icecream",Icon="🍦", Name="抹茶冰淇淋", Kind=ConsumeKind.Food, Price=38,  Hunger=18, Thirst=10, Happy=35, Line="冰淇淋!冰冰凉凉,心情一下子就好啦♪" },
        new(){ Key="feast",   Icon="🍲", Name="妖怪火锅",   Kind=ConsumeKind.Food, Price=120, Hunger=95, Thirst=20, Happy=50, Line="一大锅!吾要吃到扶墙走~主人对吾最好了!" },
    };

    public static readonly List<Consumable> Drinks = new()
    {
        new(){ Key="water",   Icon="💧", Name="清水",       Kind=ConsumeKind.Drink, Price=8,   Thirst=25, Happy=6,  Line="咕嘟咕嘟……啊,解渴了!" },
        new(){ Key="tea",     Icon="🍵", Name="抹茶",       Kind=ConsumeKind.Drink, Price=20,  Thirst=35, Happy=18, Line="抹茶微苦回甘,和主人喝茶最惬意~" },
        new(){ Key="milk",    Icon="🥛", Name="牛奶",       Kind=ConsumeKind.Drink, Price=18,  Thirst=30, Hunger=8, Happy=14, Line="牛奶!要长高高,咕嘟咕嘟!" },
        new(){ Key="juice",   Icon="🧃", Name="果汁",       Kind=ConsumeKind.Drink, Price=22,  Thirst=38, Happy=20, Line="果汁甜甜的,好喝!谢谢主人~" },
        new(){ Key="soda",    Icon="🥤", Name="弹珠汽水",   Kind=ConsumeKind.Drink, Price=25,  Thirst=42, Happy=26, Line="夏日祭的汽水!气泡在舌尖跳舞,好开心!" },
        new(){ Key="coffee",  Icon="☕", Name="咖啡",       Kind=ConsumeKind.Drink, Price=28,  Thirst=32, Happy=12, Line="苦苦的……但是提神!吾要打起精神陪主人!" },
        new(){ Key="cocoa",   Icon="🍫", Name="热可可",     Kind=ConsumeKind.Drink, Price=30,  Thirst=34, Hunger=6, Happy=28, Line="暖暖的可可,甜进心里啦~" },
        new(){ Key="smoothie",Icon="🥝", Name="水果冰沙",   Kind=ConsumeKind.Drink, Price=34,  Thirst=48, Happy=30, Line="冰沙!又冰又甜,透心凉~" },
        new(){ Key="amazake", Icon="🍶", Name="甘酒",       Kind=ConsumeKind.Drink, Price=40,  Thirst=45, Hunger=10, Happy=32, Line="神社的甘酒!暖乎乎甜丝丝,是节日的味道!" },
        new(){ Key="lemon",   Icon="🍋", Name="蜂蜜柠檬水", Kind=ConsumeKind.Drink, Price=26,  Thirst=44, Happy=22, Line="酸酸甜甜的柠檬水,一口气喝光光!" },
        new(){ Key="matchaL", Icon="🧋", Name="抹茶奶盖",   Kind=ConsumeKind.Drink, Price=36,  Thirst=40, Hunger=8, Happy=34, Line="奶盖珍珠!Q弹Q弹的,主人太懂吾了!" },
        new(){ Key="spring",  Icon="⛲", Name="灵泉圣水",   Kind=ConsumeKind.Drink, Price=90,  Thirst=95, Happy=45, Line="传说的灵泉!喝下去神清气爽,元气满满!" },
    };

    public static IEnumerable<Consumable> All => Foods.Concat(Drinks);
    public static Consumable Find(string key) => All.FirstOrDefault(c => c.Key == key);
}
