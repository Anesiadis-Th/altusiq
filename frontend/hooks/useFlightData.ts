import { useEffect, useState } from "react";
import * as signalR from "@microsoft/signalr";
import { Aircraft } from "@/types/aircraft";

const RETRY_DELAYS_MS = [1000, 2000, 5000, 10000, 30000];

interface FlightDataState {
  aircraft: Aircraft[];
  connected: boolean;
}

export function useFlightData(): FlightDataState {
  const [aircraft, setAircraft] = useState<Aircraft[]>([]);
  const [connected, setConnected] = useState(false);

  useEffect(() => {
    let stopped = false;
    let retryTimer: ReturnType<typeof setTimeout> | undefined;
    let attempt = 0;

    const connection = new signalR.HubConnectionBuilder()
      .withUrl(`${process.env.NEXT_PUBLIC_API_URL}/hubs/flights`)
      .withAutomaticReconnect([0, 2000, 10000, 30000])
      .configureLogging(signalR.LogLevel.Warning)
      .build();

    const scheduleRetry = () => {
      const delay =
        RETRY_DELAYS_MS[Math.min(attempt, RETRY_DELAYS_MS.length - 1)];
      attempt++;
      retryTimer = setTimeout(start, delay);
    };

    const start = async () => {
      try {
        await connection.start();
        if (!stopped) {
          attempt = 0;
          setConnected(true);
        }
      } catch {
        if (!stopped) scheduleRetry();
      }
    };

    connection.on("ReceiveFlightData", (data: Aircraft[]) => {
      if (!stopped) setAircraft(data);
    });

    connection.onreconnecting(() => {
      if (!stopped) setConnected(false);
    });

    connection.onreconnected(() => {
      if (!stopped) setConnected(true);
    });

    connection.onclose(() => {
      if (!stopped) {
        setConnected(false);
        scheduleRetry();
      }
    });

    start();

    return () => {
      stopped = true;
      if (retryTimer) clearTimeout(retryTimer);
      connection.stop();
    };
  }, []);

  return { aircraft, connected };
}
