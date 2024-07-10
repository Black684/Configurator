using NModbus;
using NModbus.Serial;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
            string[] portnames = SerialPort.GetPortNames();
            if (portnames.Length == 0)
            {
                MessageBox.Show("COM порты не найдены");
            }
            foreach (string portName in portnames)
            {
                ConnectCOM.Items.Add(portName);
                if (portnames[0] != null)
                {
                    ConnectCOM.SelectedItem = portnames[0];
                    ButtonConnect.IsEnabled = true;
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
                if (sensor.IsConnected == false)
                {
                    string selectedPort = (String)ConnectCOM.SelectedItem;
                    try
                    {
                        sensor.Connect(selectedPort);
                        Output.Text += "Соединение установлено.\n\n";
                        sensor.Read();
                        Output.Text += $"Адрес: {sensor.State.SensorAddress}\n";
                        Output.Text += $"Частота преобразования АЦП: {sensor.State.AdcSamplingRate}\n";
                        Output.Text += $"Номер внутреннего устройства: {sensor.State.Range}\n";
                        Output.Text += $"Размерность выходной величины: {sensor.State.Dimension}\n";
                        Output.Text += $"Постоянная демпфирования: {sensor.State.DampingConstant}\n";
                        Output.Text += $"Скорость обмена по линии связи: {sensor.State.ExchangeRate}\n";
                        Output.Text += $"Паритет: {sensor.State.Parity}\n\n";
                    }
                    catch (Exception ex)
                    {
                        Output.Text += ex.Message;
                    }
                }
                else
                {
                    sensor.Disconnect();
                }
            }

            ButtonConnect.Click += ButtonConnection;
            Dimensions.ItemsSource = Enum.GetValues(typeof(Dimension)).Cast<Dimension>().Select(DimensionToStringConverter.Default.Convert);
            Dimensions.SelectedIndex = 0;
            WriteDimension.Click += WriteDimension_Click;
            sensor.IsConnectedChanged.StartWith(sensor.IsConnected).Subscribe(UpdateViewStabe);
            UpdatePressure.Click += UpdatePressure_Click;
            sensor.StateChanged.StartWith(sensor.State).Subscribe(OnStateChanged);
            ButtonConnect.IsEnabled = false;
            AddressButton.Click += AddressButton_Click;
        }

        private void AddressButton_Click(object sender, RoutedEventArgs e)
        {
            var str = AddressTextBox.Text;
            var isHex = str.Contains("0x");
            if(isHex)
            {
                str = str.Replace("0x", "");
            }
            var numberStyle = isHex ? NumberStyles.HexNumber : NumberStyles.Number;
            var formatProvider = CultureInfo.CurrentCulture;
            if (!ushort.TryParse(str, numberStyle, formatProvider, out var address))
            {
                MessageBox.Show("Адрес должен быть целым числом.");
                return;
            }
            try
            {
                var readRegister = sensor.ReadRegister(address);
                MessageBox.Show($"Регистр: {readRegister}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Данный регистр не входит в необходимый диапазон.\n{ex}");
            }
        }

        private void OnStateChanged(SensorState state)
        {
            LabelDimension.Content = state.IsValid ? DimensionToStringConverter.Default.Convert(state.Dimension) : "-";
        }

        private void UpdateViewStabe(bool isConnected)
        {
            ButtonConnect.Content = isConnected ? "Отключение" : "Подключение";
            WriteDimension.IsEnabled = isConnected;
            Restart.IsEnabled = isConnected;
            UpdatePressure.IsEnabled = isConnected;
        }

        private void WriteDimension_Click(object sender, RoutedEventArgs e)
        {
            sensor.Write(DimensionToStringConverter.Default.Convert((string)Dimensions.SelectedItem));
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
        private readonly Subject<SensorState> stateChanged = new Subject<SensorState>();
        private SensorState state;

        public Sensor()
        {
            State = SensorState.Default;
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
            var checking = modbus.ReadHoldingRegisters(1, 0x20, 1);
            if (ByteManipulater.GetMSB(checking[0]) != 0x11)
            {
                throw new IOException("Найденное устройство не является датчиком.");
            }
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
            var dto = new SensorStateDto
            {
                AdcSamplingRate = ByteManipulater.GetMSB(result[0]),
                SensorAddress = ByteManipulater.GetLSB(result[0]),
                Range = ByteManipulater.GetMSB(result[1]),
                Dimension = ByteManipulater.GetLSB(result[1]),
                DampingConstant = ByteManipulater.GetMSB(result[2]),
                ExchangeRate = ByteManipulater.GetMSB(result[3]),
                Parity = ByteManipulater.GetLSB(result[3])
            };
            State = new SensorState(dto);
        }

        public SensorState State { get => state; private set { state = value; stateChanged.OnNext(value); } }
        public IObservable<bool> IsConnectedChanged => isConnectedChanged.AsObservable();
        public IObservable<SensorState> StateChanged => stateChanged.AsObservable();

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
            Read();
        }

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

        public ushort ReadRegister(ushort address)
        {
            return modbus.ReadHoldingRegisters(1, address, 1)[0];
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

    public class DimensionToStringConverter
    {
        private readonly Dictionary<string, Dimension> map;
        private readonly Dictionary<Dimension, string> map2;

        public DimensionToStringConverter()
        {
            map = new Dictionary<string, Dimension>
            {
                {"%", Dimension.Percent},
                {"Па", Dimension.Pascal},
                {"кПа", Dimension.kPascal},
                {"МПа", Dimension.MPascal},
                {"кг*с/см^2", Dimension.kgsoncm2},
                {"кг*с/м^2", Dimension.kgsonm2},
            };
            map2 = map.ToDictionary(x => x.Value, x => x.Key);
        }

        public Dimension Convert(string value)
        {
            return map[value];
        }

        public string Convert(Dimension value)
        {
            return map2[value];
        }

        public static DimensionToStringConverter Default { get; } = new DimensionToStringConverter();

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

    public class SensorStateDto
    {
        public byte SensorAddress { get; set; }
        public byte AdcSamplingRate { get; set; }
        public byte Range {  get; set; }
        public byte Dimension { get; set; }
        public byte DampingConstant { get; set; }
        public byte ExchangeRate { get; set; }
        public byte Parity { get; set; }
    }

    public class SensorState 
    {
        public byte SensorAddress { get; }
        public AdcSamplingRate AdcSamplingRate { get; }
        public byte Range { get; }
        public Dimension Dimension { get; }
        public DampingConstant DampingConstant { get;}
        public ExchangeRate ExchangeRate { get; }
        public Parity Parity { get; }
        public static SensorState Default { get; } = new SensorState();
        public bool IsValid { get; }

        public SensorState(byte sensorAddress, byte adcSamplingRate, byte range, byte dimension, byte dampingConstant, byte exchangeRate, byte parity)
        {
            throw new NotImplementedException();
        }
        public SensorState(SensorStateDto dto)
        {
            if (dto is null)
            {
                throw new ArgumentNullException(nameof(dto));
            }
            SensorAddress = dto.SensorAddress;
            AdcSamplingRate = Converter.Convert(dto.AdcSamplingRate);
            Range = dto.Range;
            Dimension = DimensionConverter.Default.Convert(dto.Dimension);
            DampingConstant = DampingConstantConverter.Default.Convert(dto.DampingConstant);
            ExchangeRate = ExchangeRateConverter.Default.Convert(dto.ExchangeRate);
            Parity = ParityConverter.Default.Convert(dto.Parity);
            IsValid = true;
        }

        public SensorState()
        {
            SensorAddress = 1;
            AdcSamplingRate = AdcSamplingRate.Hz8;
            Range = 0;
            Dimension = Dimension.kPascal;
            DampingConstant = DampingConstant.sec0;
            ExchangeRate = ExchangeRate.bitonsec9600;
            Parity = Parity.parity;
            IsValid = false;
        }
    }
}