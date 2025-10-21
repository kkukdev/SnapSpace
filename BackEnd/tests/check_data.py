#!/usr/bin/env python3
# -*- coding: utf-8 -*-

"""
데이터베이스 체크 스크립트
- 테이블 생성 상태 확인
- 데이터 저장/조회 테스트
- 한글 인코딩 테스트
"""

from app.database import SessionLocal, engine
from app.models.group import Group
from app.models.scan import Scan
from sqlalchemy import inspect, text
import json

def check_database_connection():
    """데이터베이스 연결 상태 확인"""
    print("🔍 데이터베이스 연결 상태 확인...")
    try:
        with engine.connect() as conn:
            result = conn.execute(text("SELECT 1"))
            print("✅ 데이터베이스 연결 성공!")
            return True
    except Exception as e:
        print(f"❌ 데이터베이스 연결 실패: {e}")
        return False

def check_tables():
    """테이블 생성 상태 확인"""
    print("\n📋 테이블 생성 상태 확인...")
    try:
        inspector = inspect(engine)
        tables = inspector.get_table_names()
        print(f"✅ 생성된 테이블: {tables}")
        
        # 각 테이블의 컬럼 정보 확인
        for table_name in tables:
            columns = inspector.get_columns(table_name)
            print(f"\n📊 {table_name} 테이블 구조:")
            for col in columns:
                print(f"  - {col['name']}: {col['type']}")
        
        return True
    except Exception as e:
        print(f"❌ 테이블 확인 실패: {e}")
        return False

def test_group_operations():
    """Group 모델 테스트"""
    print("\n🧪 Group 모델 테스트...")
    db = SessionLocal()
    try:
        # 기존 데이터 조회
        groups = db.query(Group).all()
        print(f"📊 기존 그룹 수: {len(groups)}")
        
        for group in groups:
            print(f"\n그룹 ID: {group.group_id}")
            print(f"메타데이터: {group.meta_data}")
            print(f"생성 시간: {group.created_at}")
            print(f"수정 시간: {group.updated_at}")
        
        # 새 그룹 생성 테스트
        print("\n➕ 새 그룹 생성 테스트...")
        new_group = Group(meta_data={
            'name': '테스트 그룹',
            'description': '데이터베이스 체크용 그룹',
            'category': '테스트'
        })
        db.add(new_group)
        db.commit()
        db.refresh(new_group)
        
        print(f"✅ 새 그룹 생성 성공!")
        print(f"  - 그룹 ID: {new_group.group_id}")
        print(f"  - 이름: {new_group.meta_data['name']}")
        print(f"  - 설명: {new_group.meta_data['description']}")
        print(f"  - 카테고리: {new_group.meta_data['category']}")
        
        return True
    except Exception as e:
        print(f"❌ Group 테스트 실패: {e}")
        return False
    finally:
        db.close()

def test_scan_operations():
    """Scan 모델 테스트"""
    print("\n🧪 Scan 모델 테스트...")
    db = SessionLocal()
    try:
        # 기존 스캔 데이터 조회
        scans = db.query(Scan).all()
        print(f"📊 기존 스캔 수: {len(scans)}")
        
        # 그룹이 있는지 확인
        groups = db.query(Group).all()
        if not groups:
            print("⚠️ 그룹이 없어서 스캔 테스트를 건너뜁니다.")
            return True
        
        # 새 스캔 생성 테스트
        print("\n➕ 새 스캔 생성 테스트...")
        first_group = groups[0]
        new_scan = Scan(
            scan_id="TEST_SCAN_001",
            group_id=first_group.group_id,
            meta_data={
                'title': '테스트 스캔',
                'description': '데이터베이스 체크용 스캔',
                'type': 'document'
            },
            status="UPLOADED",
            file_path="/test/path/document.pdf"
        )
        db.add(new_scan)
        db.commit()
        db.refresh(new_scan)
        
        print(f"✅ 새 스캔 생성 성공!")
        print(f"  - 스캔 ID: {new_scan.scan_id}")
        print(f"  - 그룹 ID: {new_scan.group_id}")
        print(f"  - 제목: {new_scan.meta_data['title']}")
        print(f"  - 상태: {new_scan.status}")
        
        return True
    except Exception as e:
        print(f"❌ Scan 테스트 실패: {e}")
        return False
    finally:
        db.close()

def test_korean_encoding():
    """한글 인코딩 테스트"""
    print("\n🇰🇷 한글 인코딩 테스트...")
    db = SessionLocal()
    try:
        # 한글 데이터로 새 그룹 생성
        korean_group = Group(meta_data={
            'name': '한글 테스트 그룹',
            'description': '한글 설명입니다',
            'category': '카테고리',
            'location': '서울특별시',
            'tags': ['한글', '테스트', '데이터베이스']
        })
        db.add(korean_group)
        db.commit()
        db.refresh(korean_group)
        
        print(f"✅ 한글 그룹 생성 성공!")
        print(f"  - 그룹 ID: {korean_group.group_id}")
        print(f"  - 이름: {korean_group.meta_data['name']}")
        print(f"  - 설명: {korean_group.meta_data['description']}")
        print(f"  - 위치: {korean_group.meta_data['location']}")
        print(f"  - 태그: {korean_group.meta_data['tags']}")
        
        # JSON으로 출력
        print(f"\n📄 JSON 출력:")
        print(json.dumps(korean_group.meta_data, ensure_ascii=False, indent=2))
        
        return True
    except Exception as e:
        print(f"❌ 한글 인코딩 테스트 실패: {e}")
        return False
    finally:
        db.close()

def main():
    """메인 함수"""
    print("🚀 데이터베이스 체크 시작...")
    print("=" * 50)
    
    # 각 테스트 실행
    tests = [
        ("데이터베이스 연결", check_database_connection),
        ("테이블 생성 상태", check_tables),
        ("Group 모델 테스트", test_group_operations),
        ("Scan 모델 테스트", test_scan_operations),
        ("한글 인코딩 테스트", test_korean_encoding)
    ]
    
    results = []
    for test_name, test_func in tests:
        try:
            result = test_func()
            results.append((test_name, result))
        except Exception as e:
            print(f"❌ {test_name} 실행 중 오류: {e}")
            results.append((test_name, False))
    
    # 결과 요약
    print("\n" + "=" * 50)
    print("📊 테스트 결과 요약:")
    for test_name, result in results:
        status = "✅ 성공" if result else "❌ 실패"
        print(f"  - {test_name}: {status}")
    
    success_count = sum(1 for _, result in results if result)
    total_count = len(results)
    print(f"\n🎯 전체 결과: {success_count}/{total_count} 성공")
    
    if success_count == total_count:
        print("🎉 모든 테스트가 성공했습니다!")
    else:
        print("⚠️ 일부 테스트가 실패했습니다.")

if __name__ == "__main__":
    main()
