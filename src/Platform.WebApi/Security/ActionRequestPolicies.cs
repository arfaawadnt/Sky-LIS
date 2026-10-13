using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Mvc.Controllers;
using Platform.Application.Abstractions.Security;
using Platform.Application.Behaviors;

namespace Platform.WebApi.Security;

/// <summary>
/// Validation F-17: MVC reads and checks the request body (model binding, the implicit "required" of non-nullable
/// members) before the action runs, so a caller without the permission who sends an incomplete body received
/// <c>400 VALIDATION-400</c> with the field rules instead of the <c>403</c> the MediatR pipeline gives (authorization
/// before validation, 01 §3.4). This map names, per controller action, the commands and queries the action sends: the
/// request types it constructs (<c>newobj</c>) in its own body, its async state machine, its lambdas and the helpers of
/// its controller. <see cref="Demand"/> applies the pipeline's decision to each of them before an unreadable body is
/// reported, so the order is the same on both paths. The map is read from the IL once per action and cached.
/// </summary>
public static class ActionRequestPolicies
{
    private static readonly ConcurrentDictionary<MethodInfo, IReadOnlyList<Type>> Cache = new();

    private static readonly OpCode[] OneByte = new OpCode[0x100];
    private static readonly OpCode[] TwoByte = new OpCode[0x100];

#pragma warning disable CA1810 // the opcode tables are filled from OpCodes' fields, which a field initializer cannot express
    static ActionRequestPolicies()
#pragma warning restore CA1810
    {
        foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.GetValue(null) is not OpCode opCode)
            {
                continue;
            }

            var value = unchecked((ushort)opCode.Value);
            if (value < 0x100)
            {
                OneByte[value] = opCode;
            }
            else if ((value & 0xFF00) == 0xFE00)
            {
                TwoByte[value & 0xFF] = opCode;
            }
        }
    }

    /// <summary>The gated request types (commands and queries) the action constructs, ordered by name.</summary>
    public static IReadOnlyList<Type> RequestTypesOf(MethodInfo action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return Cache.GetOrAdd(action, Scan);
    }

    /// <summary>
    /// Throws the pipeline's refusal (401, 403) when the current actor may not send one of the action's requests. An action
    /// that sends a request open to anonymous callers (sign-in, recovery) is a public entry and passes.
    /// </summary>
    public static void Demand(ControllerActionDescriptor action, IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(services);
        var requests = RequestTypesOf(action.MethodInfo);
        if (requests.Count == 0 || requests.Any(RequestAuthorization.AllowsAnonymous))
        {
            return;
        }

        var user = services.GetRequiredService<ICurrentUser>();
        var privileges = services.GetRequiredService<IUserPrivileges>();
        foreach (var request in requests)
        {
            RequestAuthorization.Demand(request, RequestAuthorization.PolicyOf(request), user, privileges);
        }
    }

    private static IReadOnlyList<Type> Scan(MethodInfo action)
    {
        var found = new HashSet<Type>();
        Visit(action, action.DeclaringType!, found, []);
        return [.. found.OrderBy(type => type.FullName, StringComparer.Ordinal)];
    }

    private static void Visit(MethodBase method, Type controller, HashSet<Type> found, HashSet<MethodBase> visited)
    {
        if (!visited.Add(method))
        {
            return;
        }

        if (method.GetCustomAttribute<StateMachineAttribute>()?.StateMachineType is { } stateMachine
            && stateMachine.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) is { } moveNext)
        {
            Visit(moveNext, controller, found, visited);
        }

        var il = method.GetMethodBody()?.GetILAsByteArray();
        if (il is null)
        {
            return;
        }

        foreach (var (opCode, token) in MethodTokens(il))
        {
            MethodBase? target;
            try
            {
                target = method.Module.ResolveMethod(
                    token,
                    method.DeclaringType is { IsGenericType: true } declaring ? declaring.GetGenericArguments() : null,
                    method is MethodInfo { IsGenericMethod: true } generic ? generic.GetGenericArguments() : null);
            }
            catch (ArgumentException)
            {
                continue;
            }

            if (target?.DeclaringType is not { } owner)
            {
                continue;
            }

            if (opCode == OpCodes.Newobj && RequestGating.IsGated(owner))
            {
                found.Add(owner);
            }
            else if (BelongsTo(owner, controller))
            {
                Visit(target, controller, found, visited);   // a helper, lambda, local function or state machine of the controller
            }
        }
    }

    /// <summary>The controller itself, a type nested in it (closures, state machines), or one of its bases in this assembly.</summary>
    private static bool BelongsTo(Type type, Type controller)
    {
        for (var candidate = type; candidate is not null; candidate = candidate.DeclaringType)
        {
            for (var level = controller; level is not null && level.Assembly == controller.Assembly; level = level.BaseType)
            {
                if (candidate == level)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>The method-token operands (call, callvirt, newobj, ldftn, …) of an IL body, with their opcodes.</summary>
    private static List<(OpCode OpCode, int Token)> MethodTokens(byte[] il)
    {
        var tokens = new List<(OpCode, int)>();
        var position = 0;
        while (position < il.Length)
        {
            var first = il[position++];
            var opCode = first == 0xFE && position < il.Length ? TwoByte[il[position++]] : OneByte[first];
            switch (opCode.OperandType)
            {
                case OperandType.InlineNone:
                    break;
                case OperandType.ShortInlineBrTarget:
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar:
                    position += 1;
                    break;
                case OperandType.InlineVar:
                    position += 2;
                    break;
                case OperandType.InlineI8:
                case OperandType.InlineR:
                    position += 8;
                    break;
                case OperandType.InlineSwitch:
                    var count = BitConverter.ToInt32(il, position);
                    position += 4 + (4 * count);
                    break;
                case OperandType.InlineMethod:
                    tokens.Add((opCode, BitConverter.ToInt32(il, position)));
                    position += 4;
                    break;
                default:
                    position += 4;
                    break;
            }
        }

        return tokens;
    }
}
