using System.Windows;

namespace XinHaiHai;

/// <summary>
/// 无聊值见底后的“抢鼠标”恶作剧:周期性把光标往桌宠方向拽、画圈、乱推,
/// 但每次只小幅偏移并留出停顿,使用者仍能把鼠标“抢”回来靠近桌宠(=陪伴)即停止。
/// 绝不阻断关机/退出:强度有限且随时可在托盘退出。
/// </summary>
public class MouseMischief
{
    bool _active;
    int _phase;
    DateTime _nextAction = DateTime.MinValue;
    double _lastUserX, _lastUserY;
    readonly Random _r = new();

    public bool Active => _active;

    public void Start()
    {
        if (_active) return;
        _active = true;
        _phase = 0;
        _nextAction = DateTime.Now;
        Native.GetCursorPos(out var p);
        _lastUserX = p.X; _lastUserY = p.Y;
        Store.Log("开始鼠标恶作剧");
    }

    public void Stop()
    {
        if (!_active) return;
        _active = false;
        Store.Log("结束鼠标恶作剧");
    }

    /// <summary>
    /// 每帧调用。petCenter=桌宠中心屏幕坐标。
    /// 返回:使用者是否“主动把鼠标移向桌宠”(判定为陪伴,应停止并加无聊值)。
    /// </summary>
    public bool Update(Point petCenter)
    {
        if (!_active) return false;
        Native.GetCursorPos(out var p);
        double cx = p.X, cy = p.Y;

        // 距桌宠足够近 → 认为使用者来陪伴了
        double dPet = Dist(cx, cy, petCenter.X, petCenter.Y);
        if (dPet < 90) { Stop(); return true; }

        if (DateTime.Now < _nextAction) { _lastUserX = cx; _lastUserY = cy; return false; }

        // 使用者是否在把光标往桌宠方向推(用户对抗)——若明显靠近,也算陪伴意图
        // 这里通过“本座推一下→停顿→看使用者有没有反推”来实现拉扯手感
        _phase++;
        double dx, dy;
        double ang = Math.Atan2(petCenter.Y - cy, petCenter.X - cx);

        if (_phase % 4 == 0)
        {
            // 画个小圈,俏皮
            double t = _phase * 0.6;
            dx = Math.Cos(t) * 26; dy = Math.Sin(t) * 26;
        }
        else
        {
            // 把光标朝桌宠方向拽一小段(引诱使用者跟过来),夹杂随机抖动
            double step = 22 + _r.NextDouble() * 20;
            dx = Math.Cos(ang) * step + (_r.NextDouble() - 0.5) * 16;
            dy = Math.Sin(ang) * step + (_r.NextDouble() - 0.5) * 16;
        }

        int nx = (int)Math.Round(cx + dx);
        int ny = (int)Math.Round(cy + dy);
        Native.SetCursorPos(nx, ny);

        _nextAction = DateTime.Now.AddMilliseconds(260 + _r.Next(0, 220)); // 留停顿,可被抢回
        _lastUserX = nx; _lastUserY = ny;
        return false;
    }

    static double Dist(double ax, double ay, double bx, double by)
        => Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));
}
