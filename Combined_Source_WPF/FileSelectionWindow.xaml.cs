using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace CodeMergerUI
{
    // 체크박스 바인딩을 위한 데이터 모델
    public class SelectableFile
    {
        public string FilePath { get; set; }
        public bool IsSelected { get; set; }
    }

    public partial class FileSelectionWindow : Window
    {
        public List<string> FinalSelectedFiles { get; private set; } = new List<string>();

        // 전체 파일 목록 원본을 보관할 리스트
        private List<SelectableFile> _allItems = new List<SelectableFile>();

        public FileSelectionWindow(List<string> allFilteredFiles, List<string> previouslySelectedFiles)
        {
            InitializeComponent();

            foreach (var file in allFilteredFiles)
            {
                // 프리셋에 선택 정보가 없으면 기본적으로 모두 체크, 있다면 해당 파일만 체크
                bool isChecked = previouslySelectedFiles == null ||
                                 previouslySelectedFiles.Count == 0 ||
                                 previouslySelectedFiles.Contains(file);

                _allItems.Add(new SelectableFile { FilePath = file, IsSelected = isChecked });
            }

            // 처음에는 원본 리스트 전체를 표시
            LstFiles.ItemsSource = _allItems;

            UpdateSelectAllCheckboxState();
        }

        // 1. 검색 및 '선택한 파일만 보기' 통합 필터링 로직
        private void ApplyFilters()
        {
            string keyword = TxtSearch.Text.Trim().ToLower();
            bool showSelectedOnly = TglShowSelectedOnly.IsChecked == true;

            // 원본 리스트에서 시작
            var filteredList = _allItems.AsEnumerable();

            // 검색어가 있을 경우 필터링
            if (!string.IsNullOrEmpty(keyword))
            {
                filteredList = filteredList.Where(x => x.FilePath.ToLower().Contains(keyword));
            }

            // 토글 버튼이 켜져 있을 경우 체크된 항목만 필터링
            if (showSelectedOnly)
            {
                filteredList = filteredList.Where(x => x.IsSelected);
            }

            // 결과를 리스트박스에 새롭게 표시
            LstFiles.ItemsSource = filteredList.ToList();

            // 필터링된 리스트 기준으로 '전체 선택' 체크박스 상태 동기화
            UpdateSelectAllCheckboxState();
        }

        // 검색어 입력 시 이벤트
        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilters();
        }

        // 토글 버튼 클릭 시 이벤트
        private void TglShowSelectedOnly_Click(object sender, RoutedEventArgs e)
        {
            ApplyFilters();
        }

        // 2. 전체 선택 / 해제 동작 로직 (현재 검색되어 보이는 목록 기준)
        private void ChkSelectAll_Click(object sender, RoutedEventArgs e)
        {
            bool isChecked = ChkSelectAll.IsChecked == true;

            // 화면에 현재 표시중인 목록만 가져오기
            var currentVisibleItems = LstFiles.ItemsSource as List<SelectableFile>;

            if (currentVisibleItems != null)
            {
                foreach (var item in currentVisibleItems)
                {
                    item.IsSelected = isChecked;
                }
                // 변경된 체크 상태를 화면(리스트박스)에 강제로 즉시 반영
                LstFiles.Items.Refresh();
            }
        }

        // 현재 표시된 리스트의 선택 상태에 따라 '전체 선택' 체크박스의 V 마크를 켜고 끄는 헬퍼 메서드
        private void UpdateSelectAllCheckboxState()
        {
            var currentVisibleItems = LstFiles.ItemsSource as List<SelectableFile>;
            if (currentVisibleItems != null && currentVisibleItems.Count > 0)
            {
                // 화면에 보이는 모든 항목이 선택되어 있다면 true
                ChkSelectAll.IsChecked = currentVisibleItems.All(x => x.IsSelected);
            }
            else
            {
                ChkSelectAll.IsChecked = false;
            }
        }

        // 3. 완료 버튼 클릭 처리
        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            // 필터링으로 인해 화면에 안 보이는 항목이 있을 수 있으므로, 
            // 현재 리스트(LstFiles.ItemsSource)가 아닌 원본 데이터(_allItems)에서 체크된 것을 모두 가져옴
            FinalSelectedFiles = _allItems.Where(x => x.IsSelected).Select(x => x.FilePath).ToList();

            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}