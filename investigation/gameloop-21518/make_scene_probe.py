from pathlib import Path
root=Path(__file__).resolve().parents[2]
source=(root/'native/reader.rs').read_text().split('\nfn main() {')[0]
extra=r'''
fn describe(r:&mut Reader,object:u64)->Result<String> {
    if object<4096{return Ok("null".into());}
    let name=r.object_name(object).unwrap_or_else(|e|format!("UNKNOWN: {e}"));let class=r.memory.raw_ptr(object+0x10)?;
    let class_name=r.object_name(class).unwrap_or_else(|e|format!("UNKNOWN: {e}")); let mut current=class;let mut props=Vec::new();let mut classes=HashSet::new();
    for _ in 0..20 {
        if current<4096||!classes.insert(current){break;}
        let owner=r.object_name(current).unwrap_or_else(|e|format!("UNKNOWN: {e}"));let mut field=r.memory.raw_ptr(current+0x58)?;let mut seen=HashSet::new();
        while field>=4096&&seen.len()<3000&&seen.insert(field) {
            let bytes=r.memory.read(field,0x80)?;let prop=match r.name(i32_at(&bytes,0x18)){Ok(s)=>s,Err(e)=>{props.push(format!("{{\"Unknown\":{},\"Field\":\"0x{field:x}\"}}",quoted(&e)));break;}};
            if prop.contains("Pawn")||prop.contains("Character")||["LocalPlayers","EncryptedLocalPlayers","Player","Pawn","Character","SpectatorPawn","PlayerState","ViewTarget","PlayerController","AcknowledgedPawn","PlayerCameraManager","OwningGameInstance","NetDriver","PersistentLevel","Levels","Actors","Health","HealthMax","TeamID","PlayerName","RootComponent","RelativeLocation","CameraCache"].contains(&prop.as_str()) {
                props.push(format!("{{\"Name\":{},\"Owner\":{},\"Offset\":{},\"Extra\":\"0x{:x}\"}}",quoted(&prop),quoted(&owner),i32_at(&bytes,0x44),u64_at(&bytes,0x70)));
            }
            field=u64_at(&bytes,0x50);
        }
        current=r.memory.raw_ptr(current+0x30)?;
    }
    Ok(format!("{{\"Address\":\"0x{object:x}\",\"Name\":{},\"Class\":{},\"Properties\":[{}]}}",quoted(&name),quoted(&class_name),props.join(",")))
}
fn main() {
    let args:Vec<String>=env::args().collect();let pid=args[1].parse().unwrap();let base=u64::from_str_radix(&args[2],16).unwrap();
    let result=(||->Result<String>{
        let mut r=Reader::new(pid,base,"21518".into())?;let engine=r.memory.ptr(base+0xf621d90)?;
        let contexts=r.memory.ptr(engine+0xc40)?;let count=r.memory.int(engine+0xc48)?;let mut worlds=Vec::new();
        if !(1..=32).contains(&count){return Err("contexts".into());}
        for i in 0..count {
            let context=r.memory.ptr(contexts+i as u64*8)?;let kind=r.memory.read(context,1)?[0];let world=r.memory.raw_ptr(context+0x2a0)?;
            let mut objects=vec![format!("\"World\":{}",describe(&mut r,world)?)];
            if world>=4096 {
                let level=r.memory.raw_ptr(world+0x30)?;let net=r.memory.raw_ptr(world+0x38)?;let gi=r.memory.raw_ptr(world+0x470)?;
                objects.push(format!("\"Level\":{}",describe(&mut r,level)?));
                objects.push(format!("\"NetDriver\":{}",describe(&mut r,net)?));
                objects.push(format!("\"GameInstance\":{}",describe(&mut r,gi)?));
                if level>=4096 {
                    let header=r.memory.read(level+0xa0,16)?;let array=u64_at(&header,0);let n=i32_at(&header,8);
                    objects.push(format!("\"LevelActors\":{{\"Array\":\"0x{array:x}\",\"Count\":{n},\"Capacity\":{}}}",i32_at(&header,12)));
                    if (1..=16384).contains(&n) {
                        let entries=r.memory.read(array,n as usize*8)?;let mut controllers=Vec::new();let mut counts=HashMap::<String,usize>::new();
                        for slot in entries.chunks_exact(8) {
                            let a=u64_at(slot,0);if a<4096 {continue;}
                            let cl=r.memory.ptr(a+0x10)?;let cn=r.object_name(cl)?;*counts.entry(cn.clone()).or_default()+=1;
                            if cn.contains("PlayerController") {
                                let lp=r.memory.raw_ptr(a+0x518)?;let pawn=r.memory.raw_ptr(a+0x4b8)?;let character=r.memory.raw_ptr(a+0x4c8)?;let camera=r.memory.raw_ptr(a+0x548)?;
                                let target=if camera>=4096 {r.memory.raw_ptr(camera+0x10a0)?}else{0};
                                controllers.push(format!("{{\"Controller\":{},\"Player\":{},\"Pawn\":{},\"Character\":{},\"Camera\":{},\"ViewTarget\":{},\"ReciprocalController\":\"0x{:x}\"}}",describe(&mut r,a)?,describe(&mut r,lp)?,describe(&mut r,pawn)?,describe(&mut r,character)?,describe(&mut r,camera)?,describe(&mut r,target)?,if lp>=4096 {r.memory.raw_ptr(lp+0x30)?}else{0}));
                            }
                        }
                        objects.push(format!("\"Controllers\":[{}]",controllers.join(",")));
                        objects.push(format!("\"ActorClasses\":{{{}}}",counts.iter().map(|(k,v)|format!("{}:{v}",quoted(k))).collect::<Vec<_>>().join(",")));
                    }
                }
                if gi>=4096 {
                    let head=r.memory.read(gi+0x48,16)?;let arr=u64_at(&head,0);let n=i32_at(&head,8);
                    objects.push(format!("\"LocalPlayerCount\":{n}"));
                    if (1..=8).contains(&n)&&arr>=4096 {
                        let lp=r.memory.ptr(arr)?;let pc=r.memory.raw_ptr(lp+0x30)?;
                        objects.push(format!("\"LocalPlayer\":{}",describe(&mut r,lp)?));
                        objects.push(format!("\"Controller\":{}",describe(&mut r,pc)?));
                        if pc>=4096 {let pawn=r.memory.raw_ptr(pc+0x528)?;let camera=r.memory.raw_ptr(pc+0x548)?;objects.push(format!("\"Pawn\":{}",describe(&mut r,pawn)?));objects.push(format!("\"Camera\":{}",describe(&mut r,camera)?));}
                    }
                }
            }
            worlds.push(format!("{{\"Type\":{kind},{} }}",objects.join(",")));
        }
        Ok(format!("{{\"Contexts\":[{}]}}",worlds.join(",")))
    })();
    match result{Ok(json)=>println!("{json}"),Err(error)=>{println!("{{\"Error\":{}}}",quoted(&error));process::exit(1);}}
}
'''
(Path(__file__).parent/'scene-probe.rs').write_text(source+extra)
