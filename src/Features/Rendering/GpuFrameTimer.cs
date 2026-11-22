using System.Runtime.InteropServices;

namespace Mandelbrot.Visualizer.Features.Rendering;

/// <summary>
/// Measures GPU shader execution time using OpenGL GL_TIME_ELAPSED timer queries.
/// Fails silently on platforms without libGL.so.1.
/// </summary>
internal sealed class GpuFrameTimer : IDisposable
{
    const uint TimeElapsed = 0x88BF;
    const uint QueryResult = 0x8866;
    const uint QueryResultAvailable = 0x8867;

    uint _query;
    bool _queryRunning;
    bool _failed;

    public bool HasFrameTime { get; private set; }
    public double LastFrameMs { get; private set; }

    public void Poll()
    {
        if (_failed || !_queryRunning)
            return;

        try
        {
            GetQueryObjectiv(_query, QueryResultAvailable, out int ready);
            if (ready == 0)
                return;

            GetQueryObjectui64(_query, QueryResult, out ulong nanoseconds);
            LastFrameMs = nanoseconds / 1_000_000.0;
            HasFrameTime = true;
            _queryRunning = false;
        }
        catch (DllNotFoundException)
        {
            _failed = true;
        }
        catch (EntryPointNotFoundException)
        {
            _failed = true;
        }
    }

    public bool Begin()
    {
        if (_failed || _queryRunning)
            return false;

        try
        {
            if (_query == 0)
                GenQueries(1, ref _query);
            BeginQuery(TimeElapsed, _query);
            return true;
        }
        catch (DllNotFoundException)
        {
            _failed = true;
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            _failed = true;
            return false;
        }
    }

    public void End(bool started)
    {
        if (!started)
            return;

        try
        {
            EndQuery(TimeElapsed);
            _queryRunning = true;
        }
        catch (DllNotFoundException)
        {
            _failed = true;
        }
        catch (EntryPointNotFoundException)
        {
            _failed = true;
        }
    }

    public void Dispose()
    {
        if (_query == 0)
            return;

        try
        {
            DeleteQueries(1, ref _query);
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }

        _query = 0;
        _queryRunning = false;
    }

    [DllImport("libGL.so.1", EntryPoint = "glGenQueries")]
    static extern void GenQueries(int count, ref uint id);

    [DllImport("libGL.so.1", EntryPoint = "glDeleteQueries")]
    static extern void DeleteQueries(int count, ref uint id);

    [DllImport("libGL.so.1", EntryPoint = "glBeginQuery")]
    static extern void BeginQuery(uint target, uint id);

    [DllImport("libGL.so.1", EntryPoint = "glEndQuery")]
    static extern void EndQuery(uint target);

    [DllImport("libGL.so.1", EntryPoint = "glGetQueryObjectiv")]
    static extern void GetQueryObjectiv(uint id, uint name, out int param);

    [DllImport("libGL.so.1", EntryPoint = "glGetQueryObjectui64v")]
    static extern void GetQueryObjectui64(uint id, uint name, out ulong param);
}
