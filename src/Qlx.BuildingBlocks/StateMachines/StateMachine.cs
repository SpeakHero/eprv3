/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 16:00:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-22 16:00:00 +08:00 中国标准时间 星期三
 * 描述：泛型配置式状态机（守卫 / 动作钩子 / 非法迁移返回错误）
 */

namespace Qlx.BuildingBlocks.StateMachines;

/// <summary>
/// 泛型配置式状态机：链式配置 + 守卫（Guard）+ 进入/退出/迁移动作钩子，非法迁移返回失败结果。
/// 非线程安全：由宿主（通常是单请求 / 单聚合根内）串行使用。
/// </summary>
/// <typeparam name="TState">状态类型（通常是 enum）。</typeparam>
/// <typeparam name="TTrigger">触发器类型（通常是 enum）。</typeparam>
/// <remarks>
/// 构造函数。
/// </remarks>
/// <param name="initialState">初始状态。</param>
public sealed class StateMachine<TState, TTrigger>(TState initialState)
    where TState : notnull
    where TTrigger : notnull
{
    private readonly Dictionary<TState, StateConfig<TState, TTrigger>> _states = [];

    /// <summary>当前状态。</summary>
    public TState Current { get; private set; } = initialState;

    /// <summary>
    /// 配置指定状态（未配置时自动创建）。
    /// </summary>
    /// <param name="state">目标状态。</param>
    /// <returns>配置对象（支持链式调用）。</returns>
    public StateConfig<TState, TTrigger> Configure(TState state)
    {
        if (!_states.TryGetValue(state, out StateConfig<TState, TTrigger>? config))
        {
            config = new StateConfig<TState, TTrigger>(state);
            _states[state] = config;
        }

        return config;
    }

    /// <summary>
    /// 触发状态迁移。
    /// </summary>
    /// <param name="trigger">触发器。</param>
    /// <returns>迁移结果（成功返回 Success，失败含 ErrorMessage）。</returns>
    public StateTransitionResult<TState> Fire(TTrigger trigger)
    {
        if (!_states.TryGetValue(Current, out StateConfig<TState, TTrigger>? currentConfig))
        {
            return StateTransitionResult<TState>.Failure($"未配置当前状态：{Current}");
        }

        if (!currentConfig.Transitions.TryGetValue(trigger, out TransitionConfig<TState>? transition))
        {
            return StateTransitionResult<TState>.Failure($"当前状态 {Current} 不允许触发 {trigger}");
        }

        if (transition.Guard is not null && !transition.Guard())
        {
            return StateTransitionResult<TState>.Failure($"守卫条件拒绝：{trigger}");
        }

        TState previous = Current;
        TState targetState = transition.TargetState;

        // 退出旧状态
        currentConfig.Exit?.Invoke(previous, targetState);

        Current = targetState;

        // 迁移动作
        transition.Action?.Invoke(previous, targetState);

        // 进入新状态
        if (_states.TryGetValue(targetState, out StateConfig<TState, TTrigger>? nextConfig))
        {
            nextConfig.Enter?.Invoke(previous, targetState);
        }

        return StateTransitionResult<TState>.Success(Current);
    }
}

/// <summary>
/// 状态配置对象：定义从本状态出发的全部迁移 + 进入 / 退出钩子。
/// </summary>
/// <typeparam name="TState">状态类型。</typeparam>
/// <typeparam name="TTrigger">触发器类型。</typeparam>
public sealed class StateConfig<TState, TTrigger>
    where TState : notnull
    where TTrigger : notnull
{
    /// <summary>本配置对应的状态。</summary>
    public TState State { get; }

    internal readonly Dictionary<TTrigger, TransitionConfig<TState>> Transitions = [];
    internal Action<TState, TState>? Enter;
    internal Action<TState, TState>? Exit;

    internal StateConfig(TState state)
    {
        State = state;
    }

    /// <summary>
    /// 允许从本状态经指定触发器迁移到目标状态。
    /// </summary>
    /// <param name="trigger">触发器。</param>
    /// <param name="targetState">目标状态。</param>
    /// <returns>当前配置（链式调用）。</returns>
    public StateConfig<TState, TTrigger> Permit(TTrigger trigger, TState targetState)
    {
        Transitions[trigger] = new TransitionConfig<TState>(targetState);
        return this;
    }

    /// <summary>
    /// 为后续 <see cref="Permit"/> 注册的迁移统一添加守卫条件。
    /// </summary>
    /// <param name="guard">守卫返回 false 时迁移被拒绝。</param>
    /// <returns>当前配置（链式调用）。</returns>
    public StateConfig<TState, TTrigger> Guard(Func<bool> guard)
    {
        foreach (TransitionConfig<TState> transition in Transitions.Values)
        {
            transition.Guard = guard;
        }

        return this;
    }

    /// <summary>
    /// 本状态的进入动作（迁移成功、状态切换后执行）。
    /// </summary>
    /// <param name="action">动作，参数：from → to。</param>
    /// <returns>当前配置（链式调用）。</returns>
    public StateConfig<TState, TTrigger> OnEntry(Action<TState, TState> action)
    {
        Enter = action;
        return this;
    }

    /// <summary>
    /// 本状态的退出动作（迁移开始、状态切换前执行）。
    /// </summary>
    /// <param name="action">动作，参数：from → to。</param>
    /// <returns>当前配置（链式调用）。</returns>
    public StateConfig<TState, TTrigger> OnExit(Action<TState, TState> action)
    {
        Exit = action;
        return this;
    }

    /// <summary>
    /// 为后续 <see cref="Permit"/> 注册的迁移统一添加迁移动作。
    /// </summary>
    /// <param name="action">迁移动作，参数：from → to。</param>
    /// <returns>当前配置（链式调用）。</returns>
    public StateConfig<TState, TTrigger> OnTransition(Action<TState, TState> action)
    {
        foreach (TransitionConfig<TState> transition in Transitions.Values)
        {
            transition.Action = action;
        }

        return this;
    }
}

/// <summary>
/// 迁移配置（内部类型）。
/// </summary>
/// <typeparam name="TState">状态类型。</typeparam>
internal sealed class TransitionConfig<TState>(TState targetState) where TState : notnull
{
    /// <summary>目标状态。</summary>
    public TState TargetState { get; } = targetState;

    /// <summary>守卫条件（null 表示无守卫）。</summary>
    public Func<bool>? Guard { get; set; }

    /// <summary>迁移动作（null 表示无动作）。</summary>
    public Action<TState, TState>? Action { get; set; }
}

/// <summary>
/// 状态迁移结果。
/// </summary>
/// <typeparam name="TState">状态类型。</typeparam>
public sealed record StateTransitionResult<TState>(bool IsSuccess, TState? NewState, string? ErrorMessage)
    where TState : notnull
{
    /// <summary>构造成功结果。</summary>
    /// <param name="newState">迁移后状态。</param>
    public static StateTransitionResult<TState> Success(TState newState)
    {
        return new(true, newState, null);
    }

    /// <summary>构造失败结果。</summary>
    /// <param name="message">失败原因。</param>
    public static StateTransitionResult<TState> Failure(string message)
    {
        return new(false, default, message);
    }
}
