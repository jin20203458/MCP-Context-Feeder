# MCP Context Feeder

MCP Context Feeder는 외부 AI 에이전트(Claude, Cursor 등)에게 다수의 참조 문서와 폴더 컨텍스트를 빠르고 효율적으로 주입하기 위해 개발된 로컬 유틸리티입니다. 
Model Context Protocol(MCP) 표준을 준수하며, 에이전트가 단 한 번의 도구 호출로 지정된 작업 목표와 필요한 코드/문서 컨텍스트를 모두 가져갈 수 있도록 지원합니다.

## 주요 기능

* **직관적인 컨텍스트 구성**: 드래그 앤 드롭 및 다중 선택(Ctrl/Shift+Click)을 통해 참조할 파일과 폴더를 쉽게 목록화할 수 있습니다.
* **실시간 토큰 예측**: 등록된 파일들의 텍스트 용량을 실시간으로 분석하여 대략적인 토큰 수와 글자 수를 제공합니다.
* **로컬 MCP 서버 내장**: SSE(Server-Sent Events) 및 JSON-RPC 기반의 표준 MCP 서버가 내장되어 별도의 복잡한 설정 없이 에이전트와 통신할 수 있습니다.
* **단일 컨텍스트 주입 도구**: `get_reference_context` 도구를 통해 현재 등록된 참조 문서의 본문 전체와 사용자의 작업 목표(Task Intent)를 에이전트에게 한 번에 전달합니다.

## 사용 방법

1. 애플리케이션을 실행합니다.
2. AI에게 참조시킬 문서나 코드 폴더를 앱 화면에 드래그 앤 드롭하여 추가합니다.
3. 에이전트에게 지시할 작업 목표를 작성합니다.
4. **서버 시작** 버튼을 눌러 로컬 MCP 서버를 가동합니다. (포트 충돌 시 자동으로 다음 사용 가능한 포트를 할당하며, 주소는 화면에 표시됩니다. 예: `http://127.0.0.1:15050/sse`)
5. 사용 중인 AI 에이전트(예: Claude Desktop)의 설정 파일에 위 SSE 엔드포인트를 MCP 서버로 등록합니다.
6. 에이전트에게 작업을 요청하면, 에이전트가 자동으로 `get_reference_context` 도구를 호출하여 컨텍스트를 획득합니다.

## 기술 스택 및 아키텍처

* **Language/Framework**: C# / .NET 8.0 / WPF
* **Architecture**: MVVM 아키텍처 패턴 (`CommunityToolkit.Mvvm` 소스 제너레이터 활용)
* **Dependency Injection**: `Microsoft.Extensions.DependencyInjection`
* **Protocol**: Model Context Protocol (MCP) 표준 호환 (SSE & JSON-RPC 2.0 로컬 서버 직접 구현)