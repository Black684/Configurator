using NModbus;
using NModbus.Serial;
using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using static System.Net.Mime.MediaTypeNames;

namespace Конфигуратор
{
    /// <summary>
    /// Логика взаимодействия для MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private SerialPort serialPort;
        private Sensor sensor;

        void SearchButton(object sender, EventArgs e)
        {

            ConnectCOM.Items.Clear();
            // получение списка СОМ портов
            string[] portnames = SerialPort.GetPortNames();
            // проверка доступных СОМ портов
            if (portnames.Length == 0)
            {
                MessageBox.Show("COM порты не найдены");
            }
            foreach (string portName in portnames)
            // для каждого из доступных портов
            {
                ConnectCOM.Items.Add(portName); // добавление порта в выпадающий список
                if (portnames[0] != null)
                {
                    ConnectCOM.SelectedItem = portnames[0]; // выбор добавленного порта
                    //ButtonConnect.Visibility = Visibility.Collapsed; // активация кнопки "Подключить порт"
                }
            }
        }

        public MainWindow()
        {
            InitializeComponent();
            serialPort = new SerialPort();
            sensor = new Sensor();
            ButtonSearch.Click += SearchButton;
            Restart.Click += (o, e) => sensor.Restart();
            bool isConnected = false;

            void ButtonConnection(object sender, EventArgs e)
            {
                if (isConnected == false)
                {
                    string selectedPort = (String)ConnectCOM.SelectedItem;
                    sensor.Connect(selectedPort);
                    isConnected = true;
                    Output.Text += "Соединение установлено.\n\n";
                    try
                    {
                        sensor.Read();
                        Output.Text += $"Адрес: {sensor.State.SensorAddress}\n";
                        Output.Text += $"Частота преобразования АЦП: {Converter.Convert(sensor.State.AdcSamplingRate)}\n";
                        Output.Text += $"Номер внутреннего устройства: {sensor.State.Range}\n";
                        Output.Text += $"Размерность выходной величины: {DimensionConverter.Default.Convert(sensor.State.Dimension)}\n";
                        Output.Text += $"Постоянная демпфирования: {DampingConstantConverter.Default.Convert(sensor.State.DampingConstant)}\n";
                        Output.Text += $"Скорость обмена по линии связи: {ExchangeRateConverter.Default.Convert(sensor.State.ExchangeRate)}\n";
                        Output.Text += $"Паритет: {ParityConverter.Default.Convert(sensor.State.Parity)}\n\n";

                    }
                    catch (Exception ex)
                    {
                        Output.Text += ex.Message;
                    }
                }
                else
                {
                    sensor.Disconnect();
                    isConnected = false;
                }
            }

            ButtonConnect.Click += ButtonConnection;
            Dimensions.ItemsSource = Enum.GetValues(typeof(Dimension));
            Dimensions.SelectedIndex = 0;
            WriteDimension.Click += WriteDimension_Click;
            sensor.IsConnectedChanged.StartWith(sensor.IsConnected).Subscribe(UpdateViewStabe);
            UpdatePressure.Click += UpdatePressure_Click;
            
        }

        private void UpdateViewStabe(bool isConnected)
        {
            ButtonConnect.Content = isConnected ? "Отключение" : "Подключение";
            WriteDimension.IsEnabled = isConnected;
            Restart.IsEnabled = isConnected;

        }

        private void WriteDimension_Click(object sender, RoutedEventArgs e)
        {
            sensor.Write((Dimension)Dimensions.SelectedItem);
        }

        private void UpdatePressure_Click(object sender, RoutedEventArgs e)
        {
            PressureValue.Content = sensor.ReadPressure();
        }
    }

    public class Sensor
    {
        NModbus.ModbusFactory factory = new NModbus.ModbusFactory();
        private IModbusSerialMaster modbus;
        private SerialPort serialPort;
        private readonly Subject<bool> isConnectedChanged = new Subject<bool>();

        public Sensor()
        {
            State = new SensorState();
        }

        public bool IsConnected { get; private set; }

        public void Connect(string portName)
        {
            serialPort = new SerialPort(portName);
            serialPort.StopBits = StopBits.One;
            serialPort.Parity = System.IO.Ports.Parity.Even;
            serialPort.Open();
            var adapter = new SerialPortAdapter(serialPort);
            adapter.ReadTimeout = 1000;
            modbus = factory.CreateRtuMaster(adapter);
            IsConnected = true;
            isConnectedChanged.OnNext(true);
        }

        public void Disconnect()
        {
            modbus = null;
            serialPort.Close();
            IsConnected = false;
            isConnectedChanged.OnNext(false);
        }

        public void Read()
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException();
            }

            var result = modbus.ReadHoldingRegisters(1, 0, 0x39);
            State.AdcSamplingRate = ByteManipulater.GetMSB(result[0]);
            State.SensorAddress = ByteManipulater.GetLSB(result[0]);
            State.Range = ByteManipulater.GetMSB(result[1]);
            State.Dimension = ByteManipulater.GetLSB(result[1]);
            State.DampingConstant = ByteManipulater.GetMSB(result[2]);
            State.ExchangeRate = ByteManipulater.GetMSB(result[3]);
            State.Parity = ByteManipulater.GetLSB(result[3]);
        }

        public SensorState State { get; }

        public void Restart()
        { 
            modbus.WriteMultipleRegisters(1, 0x1F, new ushort[] { 0x5A });
        }

        public void Write(Dimension dimension)
        {
            var byteValue = DimensionConverter.Default.Convert(dimension);
            var original = modbus.ReadHoldingRegisters(1, 1, 1);
            ushort modifed = ByteManipulater.ChangeLSB(original[0], byteValue);
            modbus.WriteMultipleRegisters(1, 1, new ushort[] { modifed });
        }
        public IObservable<bool> IsConnectedChanged => isConnectedChanged.AsObservable();

        public float ReadPressure()
        {
            var byteValue = modbus.ReadHoldingRegisters(1, 0x27, 2);
            var bytes = new byte[4];
            bytes[3] = ByteManipulater.GetMSB(byteValue[0]);
            bytes[2] = ByteManipulater.GetLSB(byteValue[0]);
            bytes[1] = ByteManipulater.GetMSB(byteValue[1]);
            bytes[0] = ByteManipulater.GetLSB(byteValue[1]);
            return BitConverter.ToSingle(bytes, 0);
        }
    }

    public enum AdcSamplingRate
    {
        Hz8,
        Hz16,
        Hz32
    }

    public enum Dimension
    {
        Percent,
        Pascal,
        kPascal,
        MPascal,
        kgsoncm2,
        kgsonm2
    }

    public enum DampingConstant
    {
        sec0,
        sec0comma2,
        sec0comma4,
        sec0comma8,
        sec1comma6,
        sec3comma2,
        sec6comma4,
        sec12comma8,
        sec26comma6,
    }

    public enum ExchangeRate
    {
        bitonsec1200, 
        bitonsec2400, 
        bitonsec4800,
        bitonsec9600,
        bitonsec19200,
        bitonsec38400,
        bitonsec57600,
        bitonsec115200,
    }

    public enum Parity
    {
        parity,
        odd,
        noparity2,
        noparity1,
    }

    public class Converter
    {
         public static AdcSamplingRate Convert(byte value) 
         {
            switch (value)
            {
                case 0:
                    return AdcSamplingRate.Hz8;
                case 1:
                    return AdcSamplingRate.Hz16;
                case 2:
                    return AdcSamplingRate.Hz32;
                default:
                    throw new ArgumentException();
            }
        }
    }

    public class DimensionConverter
    {
        private readonly Dictionary<byte, Dimension> map;
        private readonly Dictionary<Dimension, byte> map2;

        public DimensionConverter()
        {
            map = new Dictionary<byte, Dimension>
            {
                {0, Dimension.Percent},
                {1, Dimension.Pascal},
                {2, Dimension.kPascal},
                {3, Dimension.MPascal},
                {4, Dimension.kgsoncm2},
                {5, Dimension.kgsonm2},
            };
            map2 = map.ToDictionary(x => x.Value, x => x.Key);
        }

        public Dimension Convert(byte value)
        {
            return map[value];
        }

        public byte Convert(Dimension value) 
        {
            return map2[value];
        }

        public static DimensionConverter Default { get; } = new DimensionConverter();
    }

    public class DampingConstantConverter
    {
        private readonly Dictionary<byte, DampingConstant> map;
        private readonly Dictionary<DampingConstant, byte> map2;

        public DampingConstantConverter()
        {
            map = new Dictionary<byte, DampingConstant>
            {
                {0, DampingConstant.sec0},
                {1, DampingConstant.sec0comma2},
                {2, DampingConstant.sec0comma4},
                {3, DampingConstant.sec0comma8},
                {4, DampingConstant.sec1comma6},
                {5, DampingConstant.sec3comma2},
                {6, DampingConstant.sec6comma4},
                {7, DampingConstant.sec12comma8},
                {8, DampingConstant.sec26comma6},
            };
            map2 = map.ToDictionary(x => x.Value, x => x.Key);
        }

        public DampingConstant Convert(byte value) 
        {
            return map[value]; 
        }

        public byte Convert(DampingConstant value) 
        { 
            return map2[value]; 
        }

        public static DampingConstantConverter Default { get; } = new DampingConstantConverter();
    }

    public class ExchangeRateConverter
    {
        private readonly Dictionary<byte, ExchangeRate> map;
        private readonly Dictionary<ExchangeRate, byte> map2;

        public ExchangeRateConverter()
        {
            map = new Dictionary<byte, ExchangeRate>
            {
                {0, ExchangeRate.bitonsec1200},
                {1, ExchangeRate.bitonsec2400},
                {2, ExchangeRate.bitonsec4800},
                {3, ExchangeRate.bitonsec9600},
                {4, ExchangeRate.bitonsec19200},
                {5, ExchangeRate.bitonsec38400},
                {6, ExchangeRate.bitonsec57600},
                {7, ExchangeRate.bitonsec115200},
                
            };
            map2 = map.ToDictionary(x => x.Value, x => x.Key);
        }

        public ExchangeRate Convert(byte value) 
        { 
            return map[value]; 
        }

        public byte Convert(ExchangeRate value)
        { 
            return map2[value]; 
        }

        public static ExchangeRateConverter Default { get; } = new ExchangeRateConverter();
    }

    public class ParityConverter
    {
        private readonly Dictionary<byte, Parity> map;
        private readonly Dictionary<Parity, byte> map2;

        public ParityConverter()
        {
            map = new Dictionary<byte, Parity>
            {
                {0, Parity.parity},
                {1, Parity.odd},
                {2, Parity.noparity2},
                {3, Parity.noparity1},
            };
            map2 = map.ToDictionary(x => x.Value, x => x.Key);
        }

        public Parity Convert(byte value) 
        { 
            return map[value]; 
        }

        public byte Convert(Parity value) 
        { 
            return map2[value]; 
        }

        public static ParityConverter Default { get; } = new ParityConverter();
    }

    public class ByteManipulater
    {
        public static byte GetMSB(ushort value)
        {
            return (byte)((value >> 8) & 0xFF);
        } 
        public static byte GetLSB(ushort value)
        {
            return (byte)(value & 0xFF);
        }

        public static ushort ChangeLSB(ushort v, byte byteValue)
        {
            v &= 0xFF00;
            v |= byteValue;
            return v;
        }
    }

    public class SensorState
    {
        public byte SensorAddress { get; set; }
        public byte AdcSamplingRate { get; set; }
        public byte Range {  get; set; }
        public byte Dimension { get; set; }
        public byte DampingConstant { get; set; }
        public byte ExchangeRate { get; set; }
        public byte Parity { get; set; }
    }
}