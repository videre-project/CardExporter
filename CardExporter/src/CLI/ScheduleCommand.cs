/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;


namespace CardExporter.CLI;

internal static class ScheduleCommand
{
  public static async Task<int> ExecuteAsync(
    CommandLineOptions options,
    ILoggerFactory loggerFactory,
    ILogger logger,
    CancellationToken cancellationToken
  )
  {
    TimeZoneInfo timeZone = ResolveTimeZone(options.Schedule.TimeZoneId);
    IReadOnlyList<ScheduleWindow> windows = ParseWindows(options.Schedule.Windows);
    TimeSpan pollInterval = options.Schedule.PollInterval;
    if (pollInterval <= TimeSpan.Zero)
    {
      throw new ArgumentException("Schedule poll interval must be positive.");
    }

    logger.LogInformation(
      "Starting CardExporter schedule in {TimeZoneId}; polling every {PollInterval} during {ScheduleWindows}.",
      timeZone.Id,
      pollInterval,
      string.Join(", ", windows.Select(static window => window.ToDisplayString()))
    );

    while (!cancellationToken.IsCancellationRequested)
    {
      DateTime utcNow = DateTime.UtcNow;
      DateTime localNow = TimeZoneInfo.ConvertTimeFromUtc(utcNow, timeZone);
      DateTime nextRunLocal = FindNextRun(localNow, windows, pollInterval);
      DateTime nextRunUtc = TimeZoneInfo.ConvertTimeToUtc(
        DateTime.SpecifyKind(nextRunLocal, DateTimeKind.Unspecified),
        timeZone
      );
      TimeSpan delay = nextRunUtc - utcNow;

      if (delay > TimeSpan.Zero)
      {
        logger.LogInformation(
          "Next CardExporter poll is scheduled for {NextRunLocal} {TimeZoneId}.",
          nextRunLocal,
          timeZone.Id
        );
        try
        {
          await Task.Delay(delay, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
          break;
        }
      }

      if (cancellationToken.IsCancellationRequested)
      {
        break;
      }

      await RunImportPollAsync(options, loggerFactory, logger);
    }

    logger.LogInformation("CardExporter schedule stopped.");
    return 0;
  }

  private static async Task RunImportPollAsync(
    CommandLineOptions options,
    ILoggerFactory loggerFactory,
    ILogger logger
  )
  {
    CommandLineOptions importOptions = options with
    {
      Mode = CommandMode.Import,
      StartClient = !options.R2.DryRun,
      LogOn = !options.R2.DryRun,
      SyncImages = true,
      SyncAssets = true
    };

    logger.LogInformation("Starting scheduled CardExporter import poll.");
    int result;
    try
    {
      result = await Program.ExecuteOnceAsync(importOptions, loggerFactory, logger);
    }
    catch (Exception exception)
    {
      logger.LogError(exception, "Scheduled CardExporter import poll failed.");
      return;
    }

    if (result == 0)
    {
      logger.LogInformation("Scheduled CardExporter import poll completed successfully.");
      return;
    }

    logger.LogError(
      "Scheduled CardExporter import poll exited with status code {StatusCode}.",
      result
    );
  }

  private static TimeZoneInfo ResolveTimeZone(string timeZoneId)
  {
    foreach (string candidate in EnumerateTimeZoneCandidates(timeZoneId))
    {
      try
      {
        return TimeZoneInfo.FindSystemTimeZoneById(candidate);
      }
      catch (TimeZoneNotFoundException)
      {
      }
      catch (InvalidTimeZoneException)
      {
      }
    }

    throw new ArgumentException($"Could not resolve schedule time zone '{timeZoneId}'.");
  }

  private static IEnumerable<string> EnumerateTimeZoneCandidates(string timeZoneId)
  {
    yield return timeZoneId;

    if (string.Equals(timeZoneId, "America/Los_Angeles", StringComparison.OrdinalIgnoreCase))
    {
      yield return "Pacific Standard Time";
    }
    else if (string.Equals(timeZoneId, "Pacific Standard Time", StringComparison.OrdinalIgnoreCase))
    {
      yield return "America/Los_Angeles";
    }
  }

  private static IReadOnlyList<ScheduleWindow> ParseWindows(string rawWindows)
  {
    if (string.IsNullOrWhiteSpace(rawWindows))
    {
      throw new ArgumentException("Schedule windows cannot be empty.");
    }

    var windows = new List<ScheduleWindow>();
    string[] parts = rawWindows.Split(
      [';', '|'],
      StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
    );
    foreach (string part in parts)
    {
      windows.Add(ParseWindow(part));
    }

    if (windows.Count == 0)
    {
      throw new ArgumentException("Schedule windows cannot be empty.");
    }

    return windows
      .OrderBy(static window => window.DayOfWeek)
      .ThenBy(static window => window.Start)
      .ToList();
  }

  private static ScheduleWindow ParseWindow(string value)
  {
    int separator = value.IndexOf('=');
    if (separator <= 0 || separator == value.Length - 1)
    {
      throw new ArgumentException(
        $"Schedule window '{value}' must be in DAY=HH:mm-HH:mm format."
      );
    }

    string dayValue = value[..separator].Trim();
    string rangeValue = value[(separator + 1)..].Trim();
    if (!Enum.TryParse(dayValue, ignoreCase: true, out DayOfWeek dayOfWeek))
    {
      throw new ArgumentException($"Schedule window '{value}' has an invalid day.");
    }

    string[] rangeParts = rangeValue.Split(
      '-',
      StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
    );
    if (rangeParts.Length != 2 ||
        !TimeOnly.TryParseExact(
          rangeParts[0],
          "HH:mm",
          CultureInfo.InvariantCulture,
          DateTimeStyles.None,
          out TimeOnly start
        ) ||
        !TimeOnly.TryParseExact(
          rangeParts[1],
          "HH:mm",
          CultureInfo.InvariantCulture,
          DateTimeStyles.None,
          out TimeOnly end
        ))
    {
      throw new ArgumentException(
        $"Schedule window '{value}' must use HH:mm-HH:mm times."
      );
    }

    if (end <= start)
    {
      throw new ArgumentException(
        $"Schedule window '{value}' must end after it starts."
      );
    }

    return new ScheduleWindow(dayOfWeek, start, end);
  }

  private static DateTime FindNextRun(
    DateTime localNow,
    IReadOnlyList<ScheduleWindow> windows,
    TimeSpan pollInterval
  )
  {
    DateTime today = localNow.Date;
    for (int dayOffset = 0; dayOffset <= 7; dayOffset++)
    {
      DateTime date = today.AddDays(dayOffset);
      foreach (ScheduleWindow window in windows)
      {
        if (window.DayOfWeek != date.DayOfWeek)
        {
          continue;
        }

        DateTime windowStart = date.Add(window.Start.ToTimeSpan());
        DateTime windowEnd = date.Add(window.End.ToTimeSpan());
        if (dayOffset == 0 && localNow >= windowEnd)
        {
          continue;
        }

        DateTime nextRun = windowStart;
        if (dayOffset == 0 && localNow > windowStart)
        {
          long elapsedTicks = localNow.Ticks - windowStart.Ticks;
          long intervalCount = (elapsedTicks + pollInterval.Ticks - 1) / pollInterval.Ticks;
          nextRun = windowStart.AddTicks(intervalCount * pollInterval.Ticks);
        }

        if (nextRun < windowEnd)
        {
          return nextRun;
        }
      }
    }

    throw new InvalidOperationException("No future schedule window could be found.");
  }

  private sealed record ScheduleWindow(
    DayOfWeek DayOfWeek,
    TimeOnly Start,
    TimeOnly End
  )
  {
    public string ToDisplayString()
    {
      return string.Create(
        CultureInfo.InvariantCulture,
        $"{DayOfWeek}={Start:HH\\:mm}-{End:HH\\:mm}"
      );
    }
  }
}
