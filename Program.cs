// derived from original example by Cam Soper: https://www.linkedin.com/in/camthegeek
// sources: https://docs.microsoft.com/en-us/events/learntv/lets-learn-dotnet-iot-sept-2021/
// Lets Learn .Net: https://dotnet.microsoft.com/en-us/live/lets-learn-dotnet

// nuget packages: Microsoft.Azure.Devices.Client

// cd C:\Users\mwcor\Desktop\recent_work\crada\Grove-IoT-Tests\CamSoperIoTExercise
// dotnet build && dotnet publish -r linux-arm && scp .\bin\Debug\net6.0\linux-arm\publish\*  pi@rpi3b-1:~/Desktop/TestHarness

//      or right-clickt Project -> Open in terminal: ./publish-to-pi.bat  (rpi3b-1)

//      use Azure IoT explorer to visualize telemetry  

//      use wireshark to capture packets

// MQTT / Security refs 
// IOT Hub Security: https://docs.microsoft.com/en-us/azure/iot-hub/iot-concepts-and-iot-hub#device-identity-and-authentication
// MQTT:  https://docs.microsoft.com/en-us/azure/iot-hub/iot-hub-mqtt-support

// azure device connect example: https://github.com/Azure/azure-iot-sdk-csharp/blob/main/iothub/device/samples/getting%20started/SimulatedDevice/Program.cs

// enable PWM
// https://blog.oddbit.com/post/2017-09-26-some-notes-on-pwm-on-the-raspb/

using System;
using System.Device.Gpio;
using System.Device.Gpio.Drivers;
using System.Device.I2c;
using System.Text;
using Iot.Device.Bmxx80;
using Iot.Device.Bmxx80.ReadResult;
using Iot.Device.BrickPi3.Models;
using Microsoft.Azure.Devices.Client;
using Newtonsoft.Json;
using Microsoft.Extensions.Hosting;

using QSI;
using Microsoft.Extensions.Configuration;

namespace SensorTestHarness
{
    public class TestHarness
    {
        bool _fanOn = false;
        bool IsSending = false;
        bool pulse = false;
        bool _exit = false;

        int sendMessageCounter = 1;
      
        //critical section (lock) for setting the interval rate
        object IntervalLockObj;
        SensorController sensorController;
        IotHubClient iotClient;
        CancellationTokenSource ctsSendTelem;

      /*  public int GpioPin { get; set; } = 21;
        public int PwmPin { get; set; } = 18;
        public string DeviceId { get; set; } = "pi-temp-sensor-real";
      */

        short sendInterval;
        private  readonly Settings settings;


        public TestHarness(IConfiguration config)
        {
            // Get values from the config given their key and their target type.
            settings = config.GetRequiredSection("Settings").Get<Settings>();
            sendInterval = (short) settings.SendInterval;
            Console.WriteLine("Send Interval: {0}", sendInterval);

            sensorController = new SensorController(settings.GpioPin);
            iotClient = new IotHubClient(sensorController);
            ctsSendTelem = new CancellationTokenSource();

            //critical section (lock) for setting the interval rate
            IntervalLockObj = new object();
        }


        public async void Start()
        {
            Console.WriteLine("Establishing (secure) Cloud connection, please wait...");
            await iotClient.Start();
            Console.WriteLine("IoT Hub Client connected");
            sensorController.WriteStatus(_fanOn);


           
        }

        public void Loop()
        {
            while (!_exit)
            {
                string commandText = Console.ReadLine();
                HandleCommand(commandText);
            }
        }



        public async void Stop()
        {
            await iotClient.CloseAsync();

            iotClient.Dispose();
            sensorController.Dispose();
        }

        void UpdateSendInterval()
        {
            lock (IntervalLockObj)
            {
                Console.Write("Enter new send interval rate (ms): ");
                string interval = Console.ReadLine();
                if (short.TryParse(interval, out sendInterval))
                    Console.WriteLine($"Send interval rate (ms): {sendInterval}");
                else
                    Console.WriteLine($"input error, interval not set: {sendInterval}");
            }
        }
        void SendAzureCompatibleTempHumMessage(string device_id, double temp, double hum)
        {
            /* var telemetryDataPoint = new
             {
                 deviceId = device_id,
                 temperature = temp,
                 humidity = hum,
             };
            */

            THMessage telemetryDataPoint = new THMessage
            {
                deviceId = device_id,
                temperature = temp,
                humidity = hum,
                messageId = sendMessageCounter++
            };

            // serialize the telemetry data and convert it to JSON.
            var telemetryDataString = JsonConvert.SerializeObject(telemetryDataPoint);

            // Encode the serialized object using UTF-8 so it can be parsed by IoT Hub when
            // processing messaging rules.
            var message = new Message(Encoding.UTF8.GetBytes(telemetryDataString))
            {
                ContentEncoding = "utf-8",
                ContentType = "application/json",
            };

            Console.WriteLine(string.Format("[{0}] {1}", sendMessageCounter, telemetryDataString));

            // Submit the message to the hub.
            iotClient.SendDeviceToCloudMessagesAsync(message).Wait();

            /* string message_template = string.Format("{\"messageId\":{0},\"deviceId\":\"{1}\",\"temperature\":{2},\"humidity\":{3}}", ++msgCount, device_id,  temp, hum );
            Microsoft.Azure.Devices.Client.Message m = new Microsoft.Azure.Devices.Client.Message(Encoding.ASCII.GetBytes(message_template));
            iotClient.SendDeviceToCloudMessagesAsync(m).Wait();
            Debug.WriteLine(message_template);
            */
        }



        void HandleCommand(string commandText)
        {
            switch (commandText)
            {
                case "exit":
                    Console.WriteLine("Exiting!");
                    _exit = true;
                    if (IsSending)
                    {
                        ctsSendTelem.Cancel();
                        IsSending = false;
                        sensorController.WriteStatus(_fanOn);
                    }
                    break;

                case "fan":
                    _fanOn = !_fanOn;
                    sensorController.ControlLED(_fanOn);
                    sensorController.WriteStatus(_fanOn);
                    break;

                case "status":
                    sensorController.WriteStatus(_fanOn);
                    break;

                case "rate":
                    UpdateSendInterval();
                    sensorController.WriteStatus(_fanOn);
                    break;

                case "send":
                    if (!IsSending)
                    {
                        Task.Run(() =>
                        {
                            using DigitalOutput BlinkerLED = new DigitalOutput(17);
                            bool ledOn = true;
                            while (!ctsSendTelem.IsCancellationRequested)
                            {
                                lock (IntervalLockObj)
                                {
                                    BlinkerLED.ChangeState((ledOn) ? SensorStatus.On : SensorStatus.Off);
                                    double[] telem = sensorController.ReadBME280();
                                    SendAzureCompatibleTempHumMessage(settings.DeviceId, telem[0], telem[1]);

                                    Thread.Sleep((int)sendInterval);
                                    ledOn = !ledOn;
                                }
                            }

                            ctsSendTelem.Dispose();
                            ctsSendTelem = new CancellationTokenSource();
                        });

                        IsSending = true;
                        sensorController.WriteStatus(_fanOn);
                    }
                    else
                    {
                        ctsSendTelem.Cancel();
                        IsSending = false;
                        sensorController.WriteStatus(_fanOn);
                    }
                    break;


                case "pulse":
                    pulse = !pulse;
                    sensorController.ControlPWDLED(pulse);
                    sensorController.WriteStatus(_fanOn);
                    break;

                default:
                    Console.WriteLine("Command not recognized! Try again.");
                    return;
            }
        }

        /*
        void TestPWM()
        {
            var pwmController = System.Device.Pwm.PwmChannel.Create(0, 0,500, .1);

            pwmController.Start();
            int i;

            bool q = false;

            Console.CancelKeyPress += (s, e) =>
            {
                Console.WriteLine(" done...");
                e.Cancel = true;
                q = true;
            };

            Console.Write("type Ctrl-C to quit");
            while (!q)
            {
                for (i = 1; i < 10; i++)
                {
                    pwmController.DutyCycle += .1;
                    if (q) break;
                    Thread.Sleep(100);
                }

                for (; i > 1; i--)
                {
                    pwmController.DutyCycle -= .1;
                    if (q) break;
                    Thread.Sleep(100);
                }
            }

            pwmController.DutyCycle = 0.0;
            pwmController.Stop();
        }


        void WriteStatus()
        {
            // Read the BME280
            Bme280ReadResult output = bme280.Read();
            double temperatureF = output.Temperature.Value.DegreesFahrenheit;
            double humidityPercent = output.Humidity.Value.Percent;

            // Print statuses
            Console.WriteLine();
            Console.WriteLine("DEVICE STATUS");
            Console.WriteLine("-------------");
            Console.WriteLine($"Fan: {(_fanOn ? "ON" : "OFF")}");
            Console.WriteLine($"Temperature: {temperatureF:0.#}°F");
            Console.WriteLine($"Relative humidity: {humidityPercent:#.##}%");
            Console.WriteLine();
            Console.WriteLine("Enter command (status/fan/pulse/exit):");
        }
        */


        static void Main(string[] args)
        {
            // Build a config object, using env vars and JSON providers.
            // https://learn.microsoft.com/en-us/dotnet/core/extensions/configuration
            IConfiguration config = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json")
                .AddEnvironmentVariables()
                .Build();


      
            TestHarness th = new TestHarness(config);
            th.Start();
            th.Loop();
            th.Stop();
        }
    }

    public sealed class Settings
    {
        public required string ConnectionString {get; set;}

        public required int SendInterval { get; set; }
        public required int GpioPin { get; set; } 
        public required int PwmPin { get; set; } 
        public required string DeviceId { get; set; }
    }
}