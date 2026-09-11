using System;
using System.Windows;
using SpectrumAnalyzer.Core;

namespace SpectrumAnalyzer
{
    public partial class SpectrumDetailWindow : Window
    {
        public RamanSpectrumModel Model { get; private set; }

        public SpectrumDetailWindow(RamanSpectrumModel model)
        {
            InitializeComponent();
            Model = model;
            LoadModelToUI();
        }

        private void LoadModelToUI()
        {
            TxtName.Text = Model.Name;
            TxtFormula.Text = Model.NameAuto;
            TxtAcquiredAt.Text = Model.AcquiredAt;
            TxtPoints.Text = Model.Points.ToString();
            TxtLaser.Text = Model.ExcitationWavelength;
            TxtGrating.Text = Model.Grating;
            TxtPower.Text = Model.LaserPower.ToString();
            TxtTime.Text = Model.IntegrationTime.ToString();
            TxtMethod.Text = Model.ProcessMethod;
            TxtNote.Text = Model.Note;
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Model.Name = TxtName.Text;
                Model.NameAuto = TxtFormula.Text;
                Model.ExcitationWavelength = TxtLaser.Text;
                Model.Grating = TxtGrating.Text;
                Model.ProcessMethod = TxtMethod.Text;
                Model.Note = TxtNote.Text;

                if (double.TryParse(TxtPower.Text, out double power))
                    Model.LaserPower = power;
                if (double.TryParse(TxtTime.Text, out double time))
                    Model.IntegrationTime = time;

                this.DialogResult = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show("保存失败，部分数值格式不正确: " + ex.Message, "格式错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}