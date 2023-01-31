//#define SIMULATE
using Microsoft.Azure.Devices.Client;
using Newtonsoft.Json;
using System;
using System.Diagnostics;
using System.Threading.Tasks;


namespace QSI
{
    public class IotHubClient : IDisposable
    {
        public DeviceClient deviceClient;

        public SensorController sensorController { get; set; }

        public IotHubClient(SensorController sC)
        {
            sensorController = sC;
            deviceClient = DeviceClient.CreateFromConnectionString(ConnectionStringProvider.Value, TransportType.Mqtt);
        }


        public void Dispose()
        {
            deviceClient.Dispose();
        }
        
        public async Task Start()
        {
            await this.deviceClient.OpenAsync();
 
            await deviceClient.SetMethodHandlerAsync("ControlRelay", ControlRelay, null);

            // on and off (boolean) message type:  5/8/22
            await deviceClient.SetMethodHandlerAsync("ControlLED", ControlLED, null);

            //aon and off (boolean) message type:  5/8/22
            await deviceClient.SetMethodHandlerAsync("ControlPWM", ControlPWM, null);

            Debug.WriteLine("Exited!\n");
        }

        public string getDeviceId()
        {
            return "grove_iot_core_rp3";   //rasp deviceId;
        }

        public Task CloseAsync()
        {
            return this.deviceClient.CloseAsync();
        }

        public async Task SendDeviceToCloudMessagesAsync(Message message)
        {
            await deviceClient.SendEventAsync(message);
        }

        public async Task<Message> ReceiveC2dAsync()
        {
            Message receivedMessage = await deviceClient.ReceiveAsync();
            await deviceClient.CompleteAsync(receivedMessage);
            return receivedMessage;
        }

      
        private Task<MethodResponse> ControlRelay(MethodRequest methodRequest, object userContext)
        {
            Debug.WriteLine(String.Format("method ControlRelay: {0}", methodRequest.DataAsJson));
            try
            {
                OnOffMethodData m = JsonConvert.DeserializeObject<OnOffMethodData>(methodRequest.DataAsJson);
                sensorController.ControlRelay(m.onoff);
            }
            catch (Exception)
            {
             //   this.callMeLogger(String.Format("Wrong message: {0}", methodRequest.DataAsJson));
                return Task.FromResult(new MethodResponse(400));
            }
            //this.callMeLogger(methodRequest.DataAsJson);
            return Task.FromResult(new MethodResponse(200));
        }

        private Task<MethodResponse> ControlLED(MethodRequest methodRequest, object userContext)
        {
            Debug.WriteLine(String.Format("method ControlRelay: {0}", methodRequest.DataAsJson));
            try
            {
                OnOffMethodData m = JsonConvert.DeserializeObject<OnOffMethodData>(methodRequest.DataAsJson);
                sensorController.ControlLED(m.onoff);
            }
            catch (Exception)
            {
              //  this.callMeLogger(String.Format("Wrong message: {0}", methodRequest.DataAsJson));
                return Task.FromResult(new MethodResponse(400));
            }
            // this.callMeLogger(methodRequest.DataAsJson);
            return Task.FromResult(new MethodResponse(200));
        }


        private Task<MethodResponse> ControlPWM(MethodRequest methodRequest, object userContext)
        {
            Debug.WriteLine(String.Format("method ControlRelay: {0}", methodRequest.DataAsJson));
            try
            {
                OnOffMethodData m = JsonConvert.DeserializeObject<OnOffMethodData>(methodRequest.DataAsJson);
                sensorController.ControlPWDLED(m.onoff);
            }
            catch (Exception)
            {
               // this.callMeLogger(String.Format("Wrong message: {0}", methodRequest.DataAsJson));
                return Task.FromResult(new MethodResponse(400));
            }
          //  this.callMeLogger(methodRequest.DataAsJson);
            return Task.FromResult(new MethodResponse(200));
        }
    }

   
    class OnOffMethodData
    {
        public bool onoff { get; set; }
    }

}
