
//note: try this: https://docs.microsoft.com/en-us/dotnet/iot/tutorials/temp-sensor

using System;
using System.Device.Gpio;
using System.Device.Gpio.Drivers;
using System.Device.I2c;
using System.Device.Pwm;
using System.Text;
using Iot.Device.Bmxx80;
using Iot.Device.Bmxx80.ReadResult;
using Microsoft.Azure.Devices.Client;
using Newtonsoft.Json;

namespace QSI
{

    public enum SensorStatus { Off = 0, On = 1 };

    public class GPIODeviceBase
    {
        GpioController gpio_;
        int pin_;

        public GPIODeviceBase(GpioController gpio, int pin)
        {
            gpio_ = gpio;
            pin_ = pin;
        }

       
     
        public virtual void ChangeState(SensorStatus status)
        {
            if (status == SensorStatus.On)
                gpio_.Write(pin_, PinValue.High);
            else
                gpio_.Write(pin_, PinValue.Low);
        }

        public virtual SensorStatus CurrentState
        {
            get
            {
                return (pin_ == PinValue.High) ? SensorStatus.On : SensorStatus.Off;
            }
        }
    }



    public class GPIORelay : GPIODeviceBase
    {
        public GPIORelay(GpioController gpio, int pin) : base(gpio, pin)
        { }
    }

    public class GPIOLED : GPIODeviceBase
    {
        public GPIOLED(GpioController gpio, int pin) : base(gpio, pin)
        { }
    }

    // PWMdevice uses pulse widith modulation (PWM)
    // described here: https://www.mbtechworks.com/projects/raspberry-pi-pwm.html
    // http://blog.timwheeler.io/building-a-pwm-fan-controller-with-dotnet-iot/

    /* 
     * Problem: On the PI, the default Raspian configuration does not enable the PWM pin mode.
       Solution:
           Open a session to your Pi using Putty or SSH app of your choice.
           Edit the /boot/config.txt
           add "dtoverlay=pwm-2chan" to the end of the file. (Without quotes)
          Thanks to this post for the solution.
    */

    public class PWMDevice : GPIODeviceBase, IDisposable
    {
        PwmChannel pwmController;
        Task runTask;
        CancellationTokenSource ctsForStart = new CancellationTokenSource();
        bool started = false;

        public PWMDevice(int chip, int channel, int frequency = 500, double dutyCyclePerc = .1) : base(null, -1)
        {
            // chip = 0; //On the Rapsberry Pi this will be GPIO 18
            // channel = 0; //On the Rapsberry Pi this will be GPIO 18

            pwmController = PwmChannel.Create(chip, channel, frequency, dutyCyclePerc);
        }

        public override void ChangeState(SensorStatus status)
        {
            if (status == SensorStatus.On)
            {
                Start();
            }
            else
            {
                Stop();
            }
        }

        public override SensorStatus CurrentState => (started) ? SensorStatus.On : SensorStatus.Off;


        public void Dispose()
        {
            pwmController.Dispose();
            ctsForStart.Dispose();
            if (runTask != null)
                runTask.Dispose();
        }

        public void Start(int delay = 100, int iters = 10)
        {
            if (!started)
            {
                pwmController.Start();

                runTask = Task.Run(() =>
                {
                    while (!ctsForStart.IsCancellationRequested)
                    {
                        int i;
                        // increment the duty cycle by 10% each pass to 100%
                        for (i = 1; i < iters; i++)
                        {
                            pwmController.DutyCycle += .1;
                            if (ctsForStart.IsCancellationRequested)
                                break;
                            Thread.Sleep(delay);
                        }

                        // decrement the duty cycle by 10% each pass to 0%
                        for (; i > 1; i--)
                        {
                            pwmController.DutyCycle -= .1;
                            if (ctsForStart.IsCancellationRequested)
                                break;
                            Thread.Sleep(delay);
                            // Task.Delay(delay);
                        }
                    }
                });

                started = true;
            }
        }


        public void Stop(int delay = 0)
        {
            if (started)
            {
                ctsForStart.CancelAfter(delay);
                runTask.Wait();
                pwmController.DutyCycle = 0.0;
                pwmController.Stop();
                started = false;

                ctsForStart.Dispose();
                ctsForStart = new CancellationTokenSource();
            }
        }
    }

    public class DigitalOutput : IDisposable
    {
        GpioController gpio_;
        int pin_;

        public DigitalOutput(int pin)
        {
            pin_ = pin;
            gpio_ = new GpioController();
            gpio_.OpenPin(pin, PinMode.Output);
            gpio_.Write(pin_, PinValue.Low);     
        }


        public void ChangeState(SensorStatus status)
        {
            if (status == SensorStatus.On)
                gpio_.Write(pin_, PinValue.High);
            else
                gpio_.Write(pin_, PinValue.Low);
        }

        public void Dispose()
        {

            gpio_.ClosePin(pin_);
            gpio_.Dispose();
        }

        public SensorStatus CurrentState
        {
            get
            {
                return (pin_ == PinValue.High) ? SensorStatus.On : SensorStatus.Off;
            }
        }
    }


    public class SensorController : IDisposable
    {
        int _pin;
        GpioController gpio;
        Bme280 bme280;
        I2cConnectionSettings i2cSettings;
        I2cDevice i2cDevice;

        GPIORelay relay;
        GPIOLED led;
        PWMDevice pwm_led;


        public SensorController(int pin)
        {
            _pin = pin;

            // Initialize the GPIO controller
            gpio = new GpioController();

            // Open the GPIO pin for output
            gpio.OpenPin(_pin, PinMode.Output);
            gpio.Write(_pin, PinValue.Low);

            // Get a reference to a device on the I2C bus
            i2cSettings = new I2cConnectionSettings(1, Bme280.DefaultI2cAddress);
            i2cDevice = I2cDevice.Create(i2cSettings);

            // Create a reference to the BME280
            bme280 = new Bme280(i2cDevice);

            //create sensors: standard LED (on/off),  PWM LED (variable light output), Relay
            relay = new GPIORelay(gpio, _pin);
            led = new GPIOLED(gpio, _pin); // note: uses LED uses same pin as relay
            pwm_led = new PWMDevice(0, 0);   // chip = 0; //On the Rapsberry Pi this will be GPIO 18
                                             // channel = 0; //On the Rapsberry Pi this will be GPIO 18
        }

        public GpioController GetGPIOController()
        {
            return gpio;
        }


        public void WriteStatus(bool devOn = false)
        {
            // Read the BME280
            Bme280ReadResult output = bme280.Read();
            double[] vals = new double[2];

            double temperatureF = output.Temperature.Value.DegreesFahrenheit;
            double humidityPercent = output.Humidity.Value.Percent;

            // Print statuses
            Console.WriteLine();
            Console.WriteLine("DEVICE STATUS");
            Console.WriteLine("-------------");
            Console.WriteLine($"External Device  : {(devOn ? "ON" : "OFF")}");
            Console.WriteLine($"Temperature      : {temperatureF:0.#}°F");
            Console.WriteLine($"Relative humidity: {humidityPercent:#.##}%");
            Console.WriteLine();
            Console.WriteLine("Enter command (status/fan/pulse/send/rate/exit):");
        }


        //note: try this: https://docs.microsoft.com/en-us/dotnet/iot/tutorials/temp-sensor

        public double[] ReadBME280()
        {
            // Read the BME280
            Bme280ReadResult output = bme280.Read();
            double[] vals = new double[2];

           

            double temperatureF = output.Temperature.Value.DegreesFahrenheit;
            double humidityPercent = output.Humidity.Value.Percent;

            vals[0] = temperatureF;
            vals[1] = humidityPercent;
            return vals;
        }

        public void ControlPWDLED(Boolean onoff)
        {
            if (onoff)
            {
                pwm_led.ChangeState(SensorStatus.On);
            }
            else
            {
                pwm_led.ChangeState(SensorStatus.Off);
            }
        }


        public void ControlRelay(Boolean onoff)
        {
            if (onoff)
            {
                relay.ChangeState(SensorStatus.On);
            }
            else
            {
                relay.ChangeState(SensorStatus.Off);
            }
        }


        public void ControlLED(bool onoff)
        {
            if (onoff)
            {
                led.ChangeState(SensorStatus.On);
            }
            else
            {
                led.ChangeState(SensorStatus.Off);
            }
        }

        public void Dispose()
        {
            bme280.Dispose();
            i2cDevice.Dispose();

            // Close the pin before exit
            gpio.ClosePin(_pin);

            gpio.Dispose();
        }
    }
}


/* 
// extension method to extend Iot.Device.GrovePiDevice.Sensors.Buzzer interface
public static class GroveSensorExtensions
{
public static void ChangeState(this Buzzer b, SensorStatus status)
{
    if (status == SensorStatus.On)
        b.Duty = 0x25;
    else
        b.Duty = 0x00;
}
}
*/