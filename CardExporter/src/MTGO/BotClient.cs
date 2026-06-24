/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MTGOSDK.API;
using MTGOSDK.Core.Security;


namespace CardExporter.MTGO;

internal static class BotClient
{
  public static async Task<Client> StartAsync(
    ILoggerFactory loggerFactory,
    ILogger logger,
    bool logOn
  )
  {
    while (!await Client.IsOnline())
    {
      logger.LogInformation("MTGO servers are currently offline. Waiting...");
      await Task.Delay(TimeSpan.FromMinutes(30));
    }

    var client = new Client(
      new ClientOptions
      {
        CreateProcess = true,
        AcceptEULAPrompt = true
      },
      loggerFactory: loggerFactory
    );

    await Task.Delay(TimeSpan.FromSeconds(5));
    logger.LogInformation("Connected to MTGO v{Version}.", Client.Version);

    if (logOn && !client.IsConnected)
    {
      DotEnv.LoadFile();
      await client.LogOn(
        username: DotEnv.Get("USERNAME"),
        password: DotEnv.Get("PASSWORD")
      );
      logger.LogInformation("Connected as {Username}.", client.CurrentUser.Name);
    }

    return client;
  }
}
