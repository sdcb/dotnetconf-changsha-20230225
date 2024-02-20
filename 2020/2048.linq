<Query Kind="Program">
  <NuGetReference Version="1.0.6">FlysEngine.Desktop</NuGetReference>
  <NuGetReference>System.Reactive</NuGetReference>
  <Namespace>FlysEngine.Desktop</Namespace>
  <Namespace>SharpDX</Namespace>
  <Namespace>SharpDX.Animation</Namespace>
  <Namespace>SharpDX.Direct2D1</Namespace>
  <Namespace>SharpDX.DXGI</Namespace>
  <Namespace>System.Reactive</Namespace>
  <Namespace>System.Reactive.Linq</Namespace>
  <Namespace>System.Threading.Tasks</Namespace>
  <Namespace>System.Windows.Forms</Namespace>
</Query>

const int MatrixSize = 4;

static IEnumerable<(int x, int y)> MatrixPositions => Enumerable
    .Range(0, MatrixSize)
    .SelectMany(y => Enumerable.Range(0, MatrixSize)
    .Select(x => (x, y)));

static bool WithinBounds((int x, int y) i) => i.x >= 0 && i.y >= 0 && i.x < MatrixSize && i.y < MatrixSize;

void Main()
{
    using var g = new GameWindow();
    RenderLoop.Run(g, () => g.Render(1, PresentFlags.None));
}

public class GameWindow : RenderWindow
{
    Matrix Matrix = new Matrix();
    public static GameWindow Instance = null;

    public static Task CreateAnimation(float initialVal, float finalVal, float durationMs, Action<float> setter)
    {
        var tcs = new TaskCompletionSource<float>();
        Variable variable = Instance.XResource.CreateAnimation(initialVal, finalVal, durationMs / 1000);

        IDisposable subscription = null;
        subscription = Observable
            .FromEventPattern<RenderWindow, float>(Instance, nameof(Instance.UpdateLogic))
            .Select(x => x.EventArgs)
            .Subscribe(x =>
            {
                setter((float)variable.Value);
                if (variable.FinalValue == variable.Value)
                {
                    tcs.SetResult(finalVal);
                    variable.Dispose();
                    subscription.Dispose();
                }
            });

        return tcs.Task;
    }

    public GameWindow()
    {
        Instance = this;
        ClientSize = new System.Drawing.Size(400, 400);

        var keyUp = Observable.FromEventPattern<KeyEventArgs>(this, nameof(this.KeyUp))
            .Select(x => x.EventArgs.KeyCode);

        keyUp.Select(x => x switch
            {
                Keys.Left => (Direction?)Direction.Left,
                Keys.Right => Direction.Right,
                Keys.Down => Direction.Down,
                Keys.Up => Direction.Up,
                _ => null
            })
            .Where(x => x != null && !Matrix.IsInAnimation())
            .Select(x => x.Value)
            .Merge(DetectMouseGesture(this))
            .Subscribe(direction =>
            {
                Matrix.RequestDirection(direction);
                Text = $"总分：{Matrix.GetScore()}";
            });

        keyUp.Subscribe(k =>
		{
			if (k == Keys.Back)
			{
				Matrix.TryPopHistory();
			}
			else if (k == Keys.Escape)
			{
				if (MessageBox.Show("要重新开始游戏吗？", "确认", MessageBoxButtons.OKCancel) == System.Windows.Forms.DialogResult.OK)
				{
					Matrix.ReInitialize();
				}
			}
        });
    }

	protected override void OnLoad(EventArgs e)
	{
		Matrix.ReInitialize();
		Text = $"总分：{Matrix.GetScore()}";
	}

    protected override void OnUpdateLogic(float dt)
    {
        base.OnUpdateLogic(dt);
        if (Matrix.IsInAnimation()) return;

        if (Matrix.GameOver)
        {
            if (MessageBox.Show($"总分：{Matrix.GetScore()}\r\n重新开始吗？", "失败！", MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                Matrix.ReInitialize();
            }
            else
            {
                Matrix.GameOver = false;
            }
        }
        else if (!Matrix.KeepGoing && Matrix.GetCells().Any(v => v.N == 2048))
        {
            if (MessageBox.Show("您获得了2048！\r\n还想继续升级吗？", "恭喜！", MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                Matrix.KeepGoing = true;
            }
            else
            {
                Matrix.ReInitialize();
            }
        }
    }

    protected override void OnDraw(DeviceContext ctx)
    {
        ctx.Clear(new Color(0xffa0adbb));

        float fullEdge = Math.Min(ctx.Size.Width, ctx.Size.Height);
        float gap = fullEdge / (MatrixSize * 8);
        float edge = (fullEdge - gap * (MatrixSize + 1)) / MatrixSize;

        foreach (var v in MatrixPositions)
        {
            float centerX = gap + v.x * (edge + gap) + edge / 2.0f;
            float centerY = gap + v.y * (edge + gap) + edge / 2.0f;

            ctx.Transform =
                Matrix3x2.Translation(-edge / 2, -edge / 2) *
                Matrix3x2.Translation(centerX, centerY);

            ctx.FillRoundedRectangle(new RoundedRectangle
            {
                RadiusX = edge / 21,
                RadiusY = edge / 21,
                Rect = new RectangleF(0, 0, edge, edge),
            }, XResource.GetColor(new Color(0x59dae4ee)));
        }

        foreach (var c in Matrix.GetCells())
        {
            float centerX = gap + c.DisplayX * (edge + gap) + edge / 2.0f;
            float centerY = gap + c.DisplayY * (edge + gap) + edge / 2.0f;

            ctx.Transform =
                Matrix3x2.Translation(-edge / 2, -edge / 2) *
                Matrix3x2.Scaling(c.DisplaySize) *
                Matrix3x2.Translation(centerX, centerY);
            ctx.FillRectangle(new RectangleF(0, 0, edge, edge), XResource.GetColor(c.DisplayInfo.Background));

            var textLayout = XResource.TextLayouts[c.N.ToString(), c.DisplayInfo.FontSize];
            ctx.Transform =
                Matrix3x2.Translation(-textLayout.Metrics.Width / 2, -textLayout.Metrics.Height / 2) *
                Matrix3x2.Scaling(c.DisplaySize) *
                Matrix3x2.Translation(centerX, centerY);
            ctx.DrawTextLayout(Vector2.Zero, textLayout, XResource.GetColor(c.DisplayInfo.Foreground));
        }
    }
}

class Cell
{
    public int N;
    public float DisplayX, DisplayY, DisplaySize = 0;
    const float AnimationDurationMs = 120;

    public bool InAnimation =>
        (int)DisplayX != DisplayX ||
        (int)DisplayY != DisplayY ||
        (int)DisplaySize != DisplaySize;

    public Cell(int x, int y, int n)
    {
        DisplayX = x; DisplayY = y; N = n;
        _ = ShowSizeAnimation();
    }
    
    public async Task ShowSizeAnimation()
    {
        await GameWindow.CreateAnimation(DisplaySize, 1.2f, AnimationDurationMs, v => DisplaySize = v);
        await GameWindow.CreateAnimation(DisplaySize, 1.0f, AnimationDurationMs, v => DisplaySize = v);
    }

    public void MoveTo(int x, int y, int n = default)
    {
        _ = GameWindow.CreateAnimation(DisplayX, x, AnimationDurationMs, v => DisplayX = v);
        _ = GameWindow.CreateAnimation(DisplayY, y, AnimationDurationMs, v => DisplayY = v);

        if (n != default)
        {
            N = n;
            _ = ShowSizeAnimation();
        }
    }

    public DisplayInfo DisplayInfo => N switch
    {
        2 => DisplayInfo.Create(),
        4 => DisplayInfo.Create(0xede0c8ff),
        8 => DisplayInfo.Create(0xf2b179ff, 0xf9f6f2ff),
        16 => DisplayInfo.Create(0xf59563ff, 0xf9f6f2ff),
        32 => DisplayInfo.Create(0xf67c5fff, 0xf9f6f2ff),
        64 => DisplayInfo.Create(0xf65e3bff, 0xf9f6f2ff),
        128 => DisplayInfo.Create(0xedcf72ff, 0xf9f6f2ff, 45),
        256 => DisplayInfo.Create(0xedcc61ff, 0xf9f6f2ff, 45),
        512 => DisplayInfo.Create(0xedc850ff, 0xf9f6f2ff, 45),
        1024 => DisplayInfo.Create(0xedc53fff, 0xf9f6f2ff, 35),
        2048 => DisplayInfo.Create(0x3c3a32ff, 0xf9f6f2ff, 35),
        _ => DisplayInfo.Create(0x3c3a32ff, 0xf9f6f2ff, 30),
    };

	static Random r = new Random();
    public static Cell CreateRandomAt(int x, int y) => new Cell(x, y, r.NextDouble() < 0.9 ? 2 : 4);
}

class Matrix
{
    public Cell[,] CellTable;
    Stack<int[]> CellHistory = new Stack<int[]>();
    public bool GameOver, KeepGoing;
    static (int x, int y)[] Directions = new[] { (0, -1), (0, 1), (-1, 0), (1, 0) };

    public IEnumerable<Cell> GetCells()
    {
        foreach (var c in CellTable)
            if (c != null) yield return c;
    }

    public int GetScore() => GetCells().Sum(v => v.N);

    public bool IsInAnimation() => GetCells().Any(v => v.InAnimation);

    public void ReInitialize()
    {
        CellTable = new Cell[MatrixSize, MatrixSize];
        GameOver = false; KeepGoing = false; CellHistory.Clear();

        (int x, int y)[] allPos = MatrixPositions.ShuffleCopy();
        for (var i = 0; i < 2; ++i) // 2: initial cell count
        {
            CellTable[allPos[i].y, allPos[i].x] = Cell.CreateRandomAt(allPos[i].x, allPos[i].y);
        }
    }

    public void RequestDirection(Direction direction)
    {
        if (GameOver) return;

        var inorder = Enumerable.Range(0, MatrixSize);
        var dv = Directions[(int)direction];
        var tx = dv.x == 1 ? inorder.Reverse() : inorder;
        var ty = dv.y == 1 ? inorder.Reverse() : inorder;

        bool moved = false;
        int[] history = CellTable.Cast<Cell>().Select(v => v?.N ?? default).ToArray();
        foreach (var i in tx.SelectMany(x => ty.Select(y => (x, y))))
        {
            Cell cell = CellTable[i.y, i.x];
            if (cell == null) continue;

            var next = NextCellInDirection(i, dv);

            if (WithinBounds(next.target) && CellTable[next.target.y, next.target.x].N == cell.N)
            {   // 对面有方块，且可合并
                CellTable[i.y, i.x] = null;
                CellTable[next.target.y, next.target.x] = cell;
                cell.MoveTo(next.target.x, next.target.y, cell.N * 2);
                moved = true;
            }
            else if (next.prev != i) // 对面无方块，移动到prev
            {
                CellTable[i.y, i.x] = null;
                cell.MoveTo(next.prev.x, next.prev.y);
                CellTable[next.prev.y, next.prev.x] = cell;
                moved = true;
            }
        }

        if (moved)
        {
            var nextPos = MatrixPositions
                .Where(v => CellTable[v.y, v.x] == null)
                .ShuffleCopy()
                .First();
            CellHistory.Push(history);
            CellTable[nextPos.y, nextPos.x] = Cell.CreateRandomAt(nextPos.x, nextPos.y);

            if (!IsMoveAvailable()) GameOver = true;
        }
    }

    public ((int x, int y) target, (int x, int y) prev) NextCellInDirection((int x, int y) cell, (int x, int y) dv)
    {
        (int x, int y) prevCell;
        do
        {
            prevCell = cell;
            cell = (cell.x + dv.x, cell.y + dv.y);
        }
        while (WithinBounds(cell) && CellTable[cell.y, cell.x] == null);

        return (cell, prevCell);
    }

    public void TryPopHistory()
    {
        if (CellHistory.TryPop(out int[] history))
        {
            foreach (var pos in MatrixPositions)
            {
                CellTable[pos.y, pos.x] = history[pos.y * MatrixSize + pos.x] switch
                {
                    default(int) => null,
                    _ => new Cell(pos.x, pos.y, history[pos.y * MatrixSize + pos.x]),
                };
            }
        }
    }

    public bool IsMoveAvailable() => GetCells().Count() switch
    {
        MatrixSize * MatrixSize => MatrixPositions
            .SelectMany(v => Directions.Select(d => new
            {
                Position = v,
                Next = (x: v.x + d.x, y: v.y + d.y)
            }))
            .Where(x => WithinBounds(x.Position) && WithinBounds(x.Next))
            .Any(v => CellTable[v.Position.y, v.Position.x]?.N == CellTable[v.Next.y, v.Next.x]?.N), 
        _ => true, 
    };
}

struct DisplayInfo
{
    public Color Background;
    public Color Foreground;
    public float FontSize;

    public static DisplayInfo Create(uint background = 0xeee4daff, uint color = 0x776e6fff, float fontSize = 55) =>
        new DisplayInfo { Background = new Color(background), Foreground = new Color(color), FontSize = fontSize };
}

static class RandomUtil
{
    static Random r = new Random();
    public static T[] ShuffleCopy<T>(this IEnumerable<T> data)
    {
        var arr = data.ToArray();

        for (var i = arr.Length - 1; i > 0; --i)
        {
            int randomIndex = r.Next(i + 1);

            T temp = arr[i];
            arr[i] = arr[randomIndex];
            arr[randomIndex] = temp;
        }

        return arr;
    }
}

static IObservable<Direction> DetectMouseGesture(Form form)
{
    var mouseDown = Observable.FromEventPattern<MouseEventArgs>(form, nameof(form.MouseDown));
    var mouseUp = Observable.FromEventPattern<MouseEventArgs>(form, nameof(form.MouseUp));
    var mouseMove = Observable.FromEventPattern<MouseEventArgs>(form, nameof(form.MouseMove));
    const int throhold = 6;

    return mouseDown
        .SelectMany(x => mouseMove
        .TakeUntil(mouseUp)
        .Select(x => new { X = x.EventArgs.X, Y = x.EventArgs.Y })
        .ToList())
        .Select(d =>
        {
            int x = 0, y = 0;
            for (var i = 0; i < d.Count - 1; ++i)
            {
                if (d[i].X < d[i + 1].X) ++x;
                if (d[i].Y < d[i + 1].Y) ++y;
                if (d[i].X > d[i + 1].X) --x;
                if (d[i].Y > d[i + 1].Y) --y;
            }
            return (x, y);
        })
        .Select(v => new { Max = Math.Max(Math.Abs(v.x), Math.Abs(v.y)), Value = v })
        .Where(x => x.Max > throhold)
        .Select(v =>
        {
            if (v.Value.x == v.Max) return Direction.Right;
            if (v.Value.x == -v.Max) return Direction.Left;
            if (v.Value.y == v.Max) return Direction.Down;
            if (v.Value.y == -v.Max) return Direction.Up;
            throw new ArgumentOutOfRangeException(nameof(v));
        });
}

enum Direction
{
    Up, Down, Left, Right,
}