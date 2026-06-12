using System;
using System.Windows;

namespace CodeMergerUI
{
    public partial class CodeApprovalWindow : Window
    {
        public bool IsApproved { get; private set; } = false;
        public string Feedback { get; private set; } = string.Empty;

        public CodeApprovalWindow(string filePath, string proposedCode, string description)
        {
            InitializeComponent();
            
            TxtFilePath.Text = filePath;
            TxtProposedCode.Text = proposedCode;
            TxtDescription.Text = string.IsNullOrEmpty(description) ? "(설명 없음)" : description;
        }

        private void BtnApprove_Click(object sender, RoutedEventArgs e)
        {
            IsApproved = true;
            Feedback = TxtFeedback.Text.Trim();
            DialogResult = true;
            Close();
        }

        private void BtnReject_Click(object sender, RoutedEventArgs e)
        {
            IsApproved = false;
            Feedback = TxtFeedback.Text.Trim();
            
            if (string.IsNullOrEmpty(Feedback))
            {
                MessageBox.Show("반려할 때는 AI 에이전트에게 전달할 반려 피드백(사유)을 입력해 주세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DialogResult = false;
            Close();
        }
    }
}
