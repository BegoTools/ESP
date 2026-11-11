from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
source=(ROOT/'native/reader.rs').read_text().split('\nfn main() {')[0]
extra=r'''
fn describe_visibility(r:&mut Reader, object:u64, kind:&str)->Result<String> {
    if object<4096{return Ok("null".into());}
    let name=r.object_name(object)?;let class=r.memory.ptr(object+0x10)?;let class_name=r.object_name(class)?;
    let mut current=class;let mut props=Vec::new();let mut classes=HashSet::new();
    for _ in 0..24 {
        if current<4096||!classes.insert(current){break;}
        let owner=r.object_name(current)?;let mut field=r.memory.raw_ptr(current+0x58)?;let mut seen=HashSet::new();
        while field>=4096&&seen.len()<5000&&seen.insert(field) {
            let bytes=r.memory.read(field,0x80)?;let prop=match r.name(i32_at(&bytes,0x18)){Ok(s)=>s,Err(_)=>break};
            let lower=prop.to_lowercase();
            if kind=="component" || ["visib","occlu","render","sight","mesh","timeseconds","fov"].iter().any(|part|lower.contains(part)) {
                let t=u64_at(&bytes,0x10);let type_name=r.object_name(t).unwrap_or_default();let offset=i32_at(&bytes,0x44);
                let value=if (0..=0x20000).contains(&offset)&&type_name.ends_with("Property") {
                    r.memory.read(object+offset as u64,16).map(|v|v.iter().map(|n|format!("{n:02x}")).collect::<String>()).unwrap_or_default()
                } else {String::new()};
                props.push(format!("{{\"Name\":{},\"Type\":{},\"Owner\":{},\"Offset\":{},\"Extra\":\"0x{:x}\",\"ValueHex16\":{}}}",quoted(&prop),quoted(&type_name),quoted(&owner),offset,u64_at(&bytes,0x70),quoted(&value)));
            }
            field=u64_at(&bytes,0x50);
        }
        current=r.memory.raw_ptr(current+0x30)?;
    }
    let bytes=if kind=="mesh" {r.memory.read(object+0x480,64).unwrap_or_default()}else{Vec::new()};
    Ok(format!("{{\"Kind\":{},\"Address\":\"0x{object:x}\",\"Name\":{},\"Class\":{},\"Properties\":[{}],\"Mesh480\":[{}]}}",quoted(kind),quoted(&name),quoted(&class_name),props.join(","),bytes.chunks_exact(4).map(|b|number(f32_at(b,0))).collect::<Vec<_>>().join(",")))
}
fn main() {
    let args:Vec<String>=env::args().collect();let pid=args[1].parse().unwrap();let base=u64::from_str_radix(&args[2],16).unwrap();
    let result=(||->Result<String>{
        let mut r=Reader::new(pid,base,"21518".into())?;
        let engine=r.memory.ptr(base+0xf621d90)?;let contexts=r.memory.ptr(engine+0xc40)?;let count=r.memory.int(engine+0xc48)?;
        if !(1..=32).contains(&count){return Err("contexts".into());}
        let mut world=0;for i in 0..count {let context=r.memory.ptr(contexts+i as u64*8)?;if r.memory.read(context,1)?[0]==1 {world=r.memory.ptr(context+0x2a0)?;break;}}
        let net=r.memory.ptr(world+0x38)?;let connection=r.memory.ptr(net+0x78)?;let controller=r.memory.ptr(connection+0x30)?;let pawn=r.memory.ptr(controller+0x528)?;
        let mesh=r.memory.ptr(pawn+0x510)?;let mut objects=vec![describe_visibility(&mut r,world,"world")?,describe_visibility(&mut r,controller,"controller")?,describe_visibility(&mut r,pawn,"local-pawn")?,describe_visibility(&mut r,mesh,"mesh")?];
        for (holder,offset) in [(pawn,0x4800),(pawn,0x7520),(controller,0x23f8),(pawn,0x23f8)] {
            if let Ok(component)=r.memory.ptr(holder+offset) {objects.push(describe_visibility(&mut r,component,"component")?);}
        }
        let mut candidates=Vec::new();
        if let Ok(component)=r.memory.ptr(pawn+0x4800) {
            for offset in (0x350..0x620).step_by(8) {
                if let Ok(object)=r.memory.ptr(component+offset) {
                    let row=(||->Result<String>{let name=r.object_name(object)?;let class=r.memory.ptr(object+0x10)?;let class_name=r.object_name(class)?;
                        if class_name.contains("AutoAim") {objects.push(describe_visibility(&mut r,object,"component")?);}
                        Ok(format!("{{\"Offset\":{offset},\"Object\":\"0x{object:x}\",\"Name\":{},\"Class\":{}}}",quoted(&name),quoted(&class_name)))})();
                    if let Ok(row)=row {candidates.push(row);}
                }
            }
        }
        let level=r.memory.ptr(world+0x30)?;let header=r.memory.read(level+0xa0,16)?;let array=u64_at(&header,0);let n=i32_at(&header,8);
        if !(0..=16384).contains(&n){return Err("actor array".into());}
        let entries=r.memory.read(array,n as usize*8)?;let mut found=0;
        for slot in entries.chunks_exact(8) {
            let actor=u64_at(slot,0);if actor<4096||actor==pawn{continue;}
            let row=(||->Result<Option<(String,String)>>{let class=r.memory.ptr(actor+0x10)?;let info=r.class_info(class)?;if !info.player{return Ok(None);}
                let mesh=r.memory.raw_ptr(actor+0x510)?;
                Ok(Some((describe_visibility(&mut r,actor,&info.kind)?,describe_visibility(&mut r,mesh,"mesh")?)))})();
            if let Ok(Some((actor,mesh)))=row {objects.push(actor);objects.push(mesh);found+=1;if found>=6{break;}}
        }
        let mut code=Vec::new();let mut offsets=vec![0x784a770u64,0x784acf0u64,0xa88377cu64,0x5c81228u64,0x5c7cce0u64,0x5c7c698u64,0x5c7c38cu64,0x5c81a18u64];
        let mut raycast=String::from("null");
        if let Ok(component)=r.memory.ptr(pawn+0x4800) {if let Ok(table)=r.memory.ptr(component) {if let Ok(function)=r.memory.ptr(table+0x4e8) {if function>=base {offsets.push(function-base);let cache=r.memory.read(component+0x5f0,16)?;raycast=format!("{{\"FunctionRva\":\"0x{:x}\",\"CacheCount\":{}}}",function-base,i32_at(&cache,8));}}}}
        for offset in offsets {if let Ok(bytes)=r.memory.read(base+offset,4096) {code.push(format!("{{\"Rva\":\"0x{offset:x}\",\"BytesHex\":{}}}",quoted(&bytes.iter().map(|n|format!("{n:02x}")).collect::<String>())));}}
        Ok(format!("{{\"Pid\":{pid},\"Objects\":[{}],\"PrivateCandidates\":[{}],\"Raycast\":{raycast},\"Code\":[{}]}}",objects.join(","),candidates.join(","),code.join(",")))
    })();
    match result {Ok(json)=>println!("{json}"),Err(error)=>{println!("{{\"Error\":{}}}",quoted(&error));process::exit(1);}}
}
'''
(Path(__file__).parent/'visibility-probe.rs').write_text(source+extra)
