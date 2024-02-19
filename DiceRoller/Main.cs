// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using ManagedCommon;
using Microsoft.PowerToys.Settings.UI.Library;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Configuration.Internal;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using Wox.Infrastructure;
using Wox.Plugin;
using Wox.Plugin.Logger;

namespace DiceRoller;

public partial class Main : IPlugin, IPluginI18n, IContextMenu, ISettingProvider, IReloadable, IDisposable, IDelayedExecutionPlugin
{
    private const string Setting = nameof(Setting);

    // current value of the setting
    private bool _setting;

    private PluginInitContext _context;

    private string _iconPath;

    private bool _disposed;

    public string Name => Properties.Resources.plugin_name;

    public string Description => Properties.Resources.plugin_description;

    public static string PluginID => "FFD61893BFF2430CBA94E1AC007CF2E7";

    // TODO: add additional options
    public IEnumerable<PluginAdditionalOption> AdditionalOptions => new List<PluginAdditionalOption>() {
        new PluginAdditionalOption() {
            Key = Setting,
            DisplayLabel = Properties.Resources.plugin_setting,
            Value = false,
        },
    };

    // TODO: return context menus for each Result (optional, remove if not needed)
    public List<ContextMenuResult> LoadContextMenus(Result selectedResult)
    {
        return [
        ];
    }

    // TODO: return query results
    public List<Result> Query(Query query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var results = new List<Result>();

        var (total, rolls) = RollDice(query.Search);
        StringBuilder builder = new StringBuilder();
        foreach (var roll in rolls)
        {
            roll.ToString(builder);
            builder.Append(' ');
        }

        results.Add(new Result {
            Title = $"{total} = {rolls.Select(x => x.Total.ToString("+0;-#")).Aggregate((x, y) => x + y)}",
            SubTitle = builder.ToString(),
            IcoPath = _iconPath,
            Action = _ =>
            {
                Clipboard.SetText($"{total} ({query.Search}");
                return true;
            },
        });

        return results;
    }

    public static (int total, List<RollResult> results) RollDice(string diceNotation)
    {
        // Input validation:
        if (string.IsNullOrEmpty(diceNotation))
        {
            throw new ArgumentException("Dice notation cannot be null or empty.");
        }

        // Regular expression for matching multiple dice expressions:
        var match = DiceRegex().Match(diceNotation);
        if (!match.Success || match.Groups.Count < 4)
        {
            throw new ArgumentException("Invalid dice notation format. Use multiple expressions like 'XdY' separated by '+' or '-'.");
        }

        // Accumulate rolls and metadata from each expression:
        List<ParsedDiceRoll> roles = [];
        if (match.Groups[1].Success)
            roles.Add(new ParsedDiceRoll(match.Groups[1].ValueSpan));

        if (match.Groups[2].Success)
            foreach (Capture diceExpression in match.Groups[2].Captures)
                roles.Add(new ParsedDiceRoll(diceExpression.ValueSpan));

        if (match.Groups[3].Success)
            roles.Add(new ParsedDiceRoll(match.Groups[3].ValueSpan));

        // roll dice
        int total = 0;
        List<RollResult> results = [];
        foreach (var diceRoll in roles)
        {
            var roll = diceRoll.Roll();
            total += roll.Total;
            results.Add(roll);
        }
        return (total, results);
    }

    // TODO: return delayed query results (optional, remove if not needed)
    public List<Result> Query(Query query, bool delayedExecution)
    {
        ArgumentNullException.ThrowIfNull(query);

        var results = new List<Result>();

        // empty query
        if (string.IsNullOrEmpty(query.Search))
        {
            return results;
        }

        return results;
    }

    public void Init(PluginInitContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _context.API.ThemeChanged += OnThemeChanged;
        UpdateIconPath(_context.API.GetCurrentTheme());
    }

    public string GetTranslatedPluginTitle()
    {
        return Properties.Resources.plugin_name;
    }

    public string GetTranslatedPluginDescription()
    {
        return Properties.Resources.plugin_description;
    }

    private void OnThemeChanged(Theme oldtheme, Theme newTheme)
    {
        UpdateIconPath(newTheme);
    }

    private void UpdateIconPath(Theme theme)
    {
        if (theme == Theme.Light || theme == Theme.HighContrastWhite)
        {
            _iconPath = "Images/Dice.light.png";
        }
        else
        {
            _iconPath = "Images/Dice.dark.png";
        }
    }

    public Control CreateSettingPanel()
    {
        throw new NotImplementedException();
    }

    public void UpdateSettings(PowerLauncherPluginSettings settings)
    {
        _setting = settings?.AdditionalOptions?.FirstOrDefault(x => x.Key == Setting)?.Value ?? false;
    }

    public void ReloadData()
    {
        if (_context is null)
        {
            return;
        }

        UpdateIconPath(_context.API.GetCurrentTheme());
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            if (_context != null && _context.API != null)
            {
                _context.API.ThemeChanged -= OnThemeChanged;
            }

            _disposed = true;
        }
    }

    [GeneratedRegex(@"^(\d+d\d+)([+\-]\d+d\d+)*([+\-]\d+)?$")]
    private static partial Regex DiceRegex();
}
public class ParsedDiceRoll
{
    public ParsedDiceRoll(ReadOnlySpan<char> roll)
    {
        var value = roll;
        switch (value[0])
        {
            case '+':
                value = value[1..];
                Sign = 1;
                break;
            case '-':
                value = value[1..];
                Sign = -1;
                break;
            default:
                Sign = 1;
                break;
        }

        // Split the expression into number of dice and number of sides:
        var dIndex = value.IndexOf('d');
        if (dIndex == -1)
        {
            Sides = 1;
            Amount = int.Parse(value);
        }
        else
        {
            Amount = int.Parse(value[..dIndex]);
            Sides = int.Parse(value[(dIndex + 1)..]);
        }
    }

    public int Sides { get; }
    public int Amount { get; }
    public int Sign { get; }

    public override string ToString()
    {
        return $"{(Sign > 0 ? "+" : "-")}{(Sides == 1 ? Amount : $"{Amount}d{Sides}")}";
    }

    public RollResult Roll()
    {
        if (Sides == 1)
        {
            return new RollResult(this, Amount * Sign, []);
        }
        var rolls = new List<int>();
        for (var i = 0; i < Amount; i++)
        {
            rolls.Add(Random.Shared.Next(1, Sides + 1));
        }
        //todo add some removal of lowest or highest rolls
        var total = rolls.Sum();
        return new RollResult(this, total * Sign, rolls);
    }
}
public class RollResult
{
    public ParsedDiceRoll Base { get; }
    public int Total { get; }
    public List<int> Rolls { get; }

    public RollResult(ParsedDiceRoll @base, int total, List<int> rolls)
    {
        Base = @base;
        Total = total;
        Rolls = rolls;
    }

    public override string ToString()
    {
        var builder = new StringBuilder();

        foreach (var roll in Rolls)
        {
            builder
                .Append(Base.Sign > 0 ? '+' : '-')
                .Append(roll);
        }
        return builder.Append('(').Append(Base).Append(')').ToString();
    }

    public void ToString(StringBuilder builder)
    {
        foreach (var roll in Rolls)
        {
            builder
                .Append(Base.Sign > 0 ? '+' : '-')
                .Append(roll);
        }
        builder
            .Append('(')
            .Append(Base)
            .Append(')');
    }
}
