import { useEffect, useState } from "react";
import * as signalR from "@microsoft/signalr";
import { Aircraft } from "@/types/aircraft";

interface FlightDataState {
  aircraft: Aircraft[];
  connected: boolean;
  error: string | null;
}

export function useFlightData(): FlightDataState {
  const [aircraft, setAircraft] = useState<Aircraft[]>([]);
  const [connected, setConnected] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let stopped = false;

    const connection = new signalR.HubConnectionBuilder()
      .withUrl(`${process.env.NEXT_PUBLIC_API_URL}/hubs/flights`)
      .withAutomaticReconnect([0, 2000, 10000, 30000])
      .configureLogging(signalR.LogLevel.Warning)
      .build();

    connection.on("ReceiveFlightData", (data: Aircraft[]) => {
      if (!stopped) setAircraft(data);
    });

    connection.onreconnecting(() => {
      if (!stopped) setConnected(false);
    });

    connection.onreconnected(() => {
      if (!stopped) {
        setConnected(true);
        setError(null);
      }
    });

    connection.onclose(() => {
      if (!stopped) setConnected(false);
    });

    const start = async () => {
      try {
        await connection.start();
        if (!stopped) {
          setConnected(true);
          setError(null);
        }
      } catch (err) {
        if (!stopped && err instanceof Error) {
          setError(err.message);
        }
      }
    };

    start();

    return () => {
      stopped = true;
      connection.stop();
    };
  }, []);

  return { aircraft, connected, error };
}
