# 리팩토링 작업 목록

- [x] 프로젝트 파일 및 패키지 설정 (`Combined_Source_WPF.csproj` 업데이트)
- [x] 데이터 모델 생성 (`Models/ReferenceDocument.cs`, `Models/MergePreset.cs`)
- [x] 공통 파일 스캐너 인터페이스/구현 생성 (`Services/IFileInspector.cs`, `Services/FileInspector.cs`)
- [x] 프리셋 저장소 서비스 생성 (`Services/IPresetService.cs`, `Services/PresetService.cs`)
- [x] MCP 서버 클래스 이동 및 수정 (`Services/LocalContextServer.cs` 수정 및 이동, CancellationToken 적용)
- [x] `MainViewModel` 생성 (`ViewModels/MainViewModel.cs`)
- [x] `App.xaml.cs` 및 `App.xaml` 수정 (DI 설정, StartupUri 제거)
- [x] `MainWindow.xaml` 및 `MainWindow.xaml.cs` 수정 (MVVM 데이터바인딩 및 코드비하인드 제거)
- [x] 빌드 검증 및 테스트
