using System;
using System.Configuration;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MySql.Data.MySqlClient;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace WebSocketService
{
    class Program
    {
        private static NotifyIcon notifyIcon;
        private static ClientWebSocket webSocket;
        private static CancellationTokenSource cancellationTokenSource;

        static void Main()
        {
            notifyIcon = new NotifyIcon();
            notifyIcon.Icon = SystemIcons.Application;
            notifyIcon.Text = "WebSocket Service";
            notifyIcon.Visible = true;
            notifyIcon.ContextMenuStrip = new ContextMenuStrip();
            notifyIcon.ContextMenuStrip.Items.Add("Exit", null, Exit);

            // Initialize the WebSocket and start the background task
            webSocket = new ClientWebSocket();
            cancellationTokenSource = new CancellationTokenSource();
            Task.Run(WebSocketBackgroundTask);

            Application.Run();
        }

        private static async Task WebSocketBackgroundTask()
        {
            try
            {
                // Connect to the WebSocket server
                Uri uri = new Uri(ConfigurationManager.AppSettings["WebSocketUrl"]);
                await webSocket.ConnectAsync(uri, cancellationTokenSource.Token);

                while (webSocket.State == WebSocketState.Open)
                {
                    // Send ping message
                    byte[] pingBuffer = Encoding.UTF8.GetBytes("ping");
                    await webSocket.SendAsync(new ArraySegment<byte>(pingBuffer), WebSocketMessageType.Text, true, cancellationTokenSource.Token);

                    await ReceiveMessages();
                }
            }
            catch (Exception ex)
            {
                // Handle connection errors
                Console.WriteLine($"WebSocket connection error: {ex.Message}");
            }
            finally
            {
                // Clean up resources
                webSocket.Dispose();
                cancellationTokenSource.Dispose();
            }
        }

        private static async Task ReceiveMessages()
        {
            var buffer = new byte[1024];
            var message = new StringBuilder();

            WebSocketReceiveResult result;
            do
            {
                result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationTokenSource.Token);
                if (result.MessageType == WebSocketMessageType.Text)
                {
                    message.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    if (result.EndOfMessage)
                    {
                        // Process received message
                        await ProcessMessage(message.ToString());
                        message.Clear();
                    }
                }
            }
            while (!result.CloseStatus.HasValue);

            await webSocket.CloseAsync(result.CloseStatus.Value, result.CloseStatusDescription, cancellationTokenSource.Token);
        }

        private static async Task ProcessMessage(string message)
        {
            if (message == "ping")
            {
                // Respond to ping message
                byte[] pongBuffer = Encoding.UTF8.GetBytes("pong");
                await webSocket.SendAsync(new ArraySegment<byte>(pongBuffer), WebSocketMessageType.Text, true, cancellationTokenSource.Token);
            }
            else
            {
                // Handle other received messages
                // Example: Query local MySQL database and send data back to the WebSocket server

                // Parse the received JSON message
                JObject json = JObject.Parse(message);
                string database = json["database"].ToString();
                string table = json["table"].ToString();

                try
                {

                    string connectionString = ConfigurationManager.ConnectionStrings["AdventureWorksConnection"].ConnectionString;
                    using (MySqlConnection connection = new MySqlConnection(connectionString))
                    {
                        connection.Open();

                        string query = $"SELECT * FROM `{database}`.`{table}`";
                        using (MySqlCommand command = new MySqlCommand(query, connection))
                        {
                            using (MySqlDataReader reader = command.ExecuteReader())
                            {
                                // Convert the query result to JSON
                                JArray jsonArray = new JArray();
                                while (reader.Read())
                                {
                                    JObject row = new JObject();
                                    for (int i = 0; i < reader.FieldCount; i++)
                                    {
                                        row[reader.GetName(i)] = JToken.FromObject(reader.GetValue(i));
                                    }
                                    jsonArray.Add(row);
                                }

                                // Send the JSON data back to the WebSocket server
                                string jsonData = jsonArray.ToString(Formatting.None);
                                byte[] jsonBuffer = Encoding.UTF8.GetBytes(jsonData);
                                await webSocket.SendAsync(new ArraySegment<byte>(jsonBuffer), WebSocketMessageType.Text, true, cancellationTokenSource.Token);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Handle database query errors
                    Console.WriteLine($"Database query error: {ex.Message}");
                   
                }
            }
        }

        private static void Exit(object sender, EventArgs e)
        {
            Application.Exit();
        }
    }
}
