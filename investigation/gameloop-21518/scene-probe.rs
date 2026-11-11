//! Read-only, exact-build Unreal snapshot transport for the locally inspected PUBG build.
//! No injection, writes to process memory, or process-control operations are performed.
use std::collections::{HashMap, HashSet};
use std::env;
use std::fs::{self, File};
use std::io::{self, Read, Write};
use std::os::unix::fs::FileExt;
use std::process;
use std::rc::Rc;
use std::sync::{Arc, atomic::{AtomicBool, AtomicU64, Ordering}};
use std::thread;
use std::time::{Duration, Instant};

type Result<T> = std::result::Result<T, String>;
const MAX_ACTORS: usize = 16_384;
const MAX_CACHE: usize = 20_000;
const MAX_STREAM_SECONDS: u64 = 60;
const HEARTBEAT_TIMEOUT_MILLISECONDS: u64 = 3_000;

struct Heartbeat { last_ping: Arc<AtomicU64>, input_closed: Arc<AtomicBool> }
impl Heartbeat {
    fn start(clock: Instant) -> Self {
        let last_ping=Arc::new(AtomicU64::new(0));
        let input_closed=Arc::new(AtomicBool::new(false));
        let thread_ping=Arc::clone(&last_ping); let thread_closed=Arc::clone(&input_closed);
        thread::spawn(move || {
            let mut input=io::stdin().lock(); let mut bytes=[0u8;128]; let mut line=Vec::with_capacity(16); let mut overflow=false;
            loop {
                match input.read(&mut bytes) {
                    Ok(0)|Err(_) => {thread_closed.store(true,Ordering::Relaxed);break;},
                    Ok(count) => for byte in &bytes[..count] {
                        if *byte==b'\n' {
                            if !overflow && (line==b"PING" || line==b"PING\r") {thread_ping.store(clock.elapsed().as_millis() as u64,Ordering::Relaxed);}
                            line.clear(); overflow=false;
                        } else if line.len()<64 {line.push(*byte);} else {overflow=true;}
                    },
                }
            }
        });
        Self {last_ping,input_closed}
    }
    fn expired(&self, clock: Instant) -> bool {
        self.input_closed.load(Ordering::Relaxed) || (clock.elapsed().as_millis() as u64).saturating_sub(self.last_ping.load(Ordering::Relaxed))>HEARTBEAT_TIMEOUT_MILLISECONDS
    }
}

#[derive(Clone, Copy, Default)]
struct Vec3 { x: f32, y: f32, z: f32 }
impl Vec3 {
    fn valid(self) -> bool { self.x.is_finite() && self.y.is_finite() && self.z.is_finite() }
    fn json(self) -> String { format!("{{\"X\":{},\"Y\":{},\"Z\":{}}}", number(self.x), number(self.y), number(self.z)) }
}
fn number(value: f32) -> String { if value.is_finite() { value.to_string() } else { "0".into() } }
fn quoted(s: &str) -> String {
    let mut out = String::from("\"");
    for c in s.chars() {
        match c {
            '"' => out.push_str("\\\""), '\\' => out.push_str("\\\\"),
            '\n' => out.push_str("\\n"), '\r' => out.push_str("\\r"), '\t' => out.push_str("\\t"),
            c if (c as u32) < 0x20 => out.push_str(&format!("\\u{:04x}", c as u32)),
            c => out.push(c),
        }
    }
    out.push('"'); out
}
fn friendly_class(class: &str) -> String {
    let mut text=class.strip_suffix("_C").unwrap_or(class);
    for prefix in ["PickUp_","BP_","VH_"] {text=text.strip_prefix(prefix).unwrap_or(text);}
    for suffix in ["_Wrapper","_Pickup","_PickUp","Wrapper"] {text=text.strip_suffix(suffix).unwrap_or(text);}
    text.replace('_'," ")
}
fn loot_group(class: &str) -> &'static str {
    if class.contains("Ammo_") {"Ammo"}
    else if class.contains("Helmet_") || class.contains("Armor_") {"Armor"}
    else if class.contains("Bag_") {"Equipment"}
    else if class.contains("MZJ_") {"Optic"}
    else if ["Drink_Pickup_C","Bandage_Pickup_C"].contains(&class) {"Healing"}
    else if ["Rifle_","Pistol_","MachineGun_","ShotGun_","Sniper_","Grenade_","Other_DP28_"].iter().any(|part|class.contains(part)) && class.contains("Wrapper") {"Weapon"}
    else {"Other"}
}
fn u64_at(b: &[u8], offset: usize) -> u64 { u64::from_le_bytes(b[offset..offset+8].try_into().unwrap()) }
fn i32_at(b: &[u8], offset: usize) -> i32 { i32::from_le_bytes(b[offset..offset+4].try_into().unwrap()) }
fn f32_at(b: &[u8], offset: usize) -> f32 { f32::from_le_bytes(b[offset..offset+4].try_into().unwrap()) }

struct Memory { file: File, reads: u64 }
impl Memory {
    fn read(&mut self, address: u64, count: usize) -> Result<Vec<u8>> {
        if address < 4096 || count == 0 || count > 131_072 || address.checked_add(count as u64).is_none_or(|end| end > 0x0000_8000_0000_0000) {
            return Err(format!("Invalid bounded read at 0x{address:x}, count {count}"));
        }
        let mut bytes = vec![0u8; count]; let mut offset = 0;
        while offset < count {
            match self.file.read_at(&mut bytes[offset..], address + offset as u64) {
                Ok(0) => return Err(format!("Short process read at 0x{address:x}")),
                Ok(n) => offset += n,
                Err(e) => return Err(format!("Process read failed at 0x{address:x}: {e}")),
            }
        }
        self.reads += 1; Ok(bytes)
    }
    fn ptr(&mut self, a: u64) -> Result<u64> { let b = self.read(a, 8)?; let p = u64_at(&b, 0); if p < 4096 { Err(format!("Null pointer at 0x{a:x}")) } else { Ok(p) } }
    fn raw_ptr(&mut self, a: u64) -> Result<u64> { Ok(u64_at(&self.read(a,8)?,0)) }
    fn int(&mut self, a: u64) -> Result<i32> { Ok(i32_at(&self.read(a,4)?,0)) }
    fn float(&mut self, a: u64) -> Result<f32> { Ok(f32_at(&self.read(a,4)?,0)) }
    fn vec(&mut self, a: u64) -> Result<Vec3> {
        let b = self.read(a,12)?; let v = Vec3 { x:f32_at(&b,0), y:f32_at(&b,4), z:f32_at(&b,8) };
        if v.valid() { Ok(v) } else { Err(format!("Invalid vector at 0x{a:x}")) }
    }
}

#[derive(Clone)]
struct ClassInfo { name: String, player: bool, vehicle: bool, loot: bool, capsule: bool, kind: String }
struct Reader {
    memory: Memory, base: u64, names: u64, session: String, last_world: u64,
    name_cache: HashMap<i32,String>, class_cache: HashMap<u64,Rc<ClassInfo>>,
    classification_at: Option<Instant>, static_refresh_at: Option<Instant>,
    classification_signature: Vec<(u64,u64,usize)>, dynamic_actors: Vec<u64>,
}
impl Reader {
    fn new(pid: u32, base: u64, session: String) -> Result<Self> {
        let maps = fs::read_to_string(format!("/proc/{pid}/maps")).map_err(|e|format!("Cannot read process maps: {e}"))?;
        let matched = maps.lines().any(|line| {
            let fields: Vec<&str> = line.split_whitespace().collect();
            if fields.len() < 6 || !fields.last().is_some_and(|p|p.ends_with("/libUE4.so")) { return false; }
            let start = fields[0].split('-').next().and_then(|v|u64::from_str_radix(v,16).ok());
            let offset = u64::from_str_radix(fields[2],16).ok();
            match (start,offset) { (Some(s),Some(o)) => s.checked_sub(o)==Some(base), _=>false }
        });
        if !matched { return Err("Module base does not match libUE4.so process maps".into()); }
        let file = File::open(format!("/proc/{pid}/mem")).map_err(|e|format!("Cannot open process memory: {e}"))?;
        if env::var("ESP_RESTORE_ENFORCING").as_deref()==Ok("1") {
            let restored=process::Command::new("/system/bin/sh").args(["-c","setenforce 1 && getenforce"]).output().map_err(|e|format!("Cannot restore Android enforcing mode: {e}"))?;
            if !restored.status.success() || String::from_utf8_lossy(&restored.stdout).trim()!="Enforcing" {
                return Err("Android enforcing mode was not restored; refusing to read game data".into());
            }
        }
        let mut memory = Memory {file, reads:0};
        let state = base+0xf354be0; let encoded = memory.int(state)?;
        if encoded < 100 || (encoded-100)%3 != 0 || (encoded-100)/3 > 20 { return Err("Invalid exact-build names indirection".into()); }
        let mut names = memory.ptr(state+8)?;
        for _ in 0..(encoded-100)/3 { names = memory.ptr(names)?; }
        let mut reader = Reader {memory,base,names,session,last_world:0,name_cache:HashMap::new(),class_cache:HashMap::new(),classification_at:None,static_refresh_at:None,classification_signature:Vec::new(),dynamic_actors:Vec::new()};
        if reader.name(0)? != "None" { return Err("Name table sentinel validation failed".into()); }
        Ok(reader)
    }
    fn name(&mut self, index: i32) -> Result<String> {
        if let Some(s) = self.name_cache.get(&index) {return Ok(s.clone());}
        if !(0..=2_000_000).contains(&index) {return Err("Invalid FName index".into());}
        let chunk = self.memory.ptr(self.names+(index as u64/0x4000)*8)?;
        let entry = self.memory.ptr(chunk+(index as u64%0x4000)*8)?;
        let wide = self.memory.int(entry+8)? & 1 != 0;
        let mut raw = Vec::new(); let mut current = entry+12;
        while raw.len() < 192 {
            let count = (32usize).min(4096-(current as usize&4095)).min(192-raw.len());
            raw.extend(self.memory.read(current,count)?); current += count as u64;
            if wide {
                if raw.chunks_exact(2).any(|b|b[0]==0&&b[1]==0) {break;}
            } else if raw.contains(&0) {break;}
        }
        let text = if wide {
            let chars:Vec<u16> = raw.chunks_exact(2).map(|b|u16::from_le_bytes([b[0],b[1]])).take_while(|c|*c!=0).collect();
            String::from_utf16_lossy(&chars)
        } else {String::from_utf8_lossy(&raw[..raw.iter().position(|v|*v==0).unwrap_or(raw.len())]).into_owned()};
        if self.name_cache.len() >= MAX_CACHE { self.name_cache.clear(); }
        self.name_cache.insert(index,text.clone()); Ok(text)
    }
    fn object_name(&mut self, object:u64) -> Result<String> {let index=self.memory.int(object+0x18)?; self.name(index)}
    fn class_info(&mut self, class:u64) -> Result<Rc<ClassInfo>> {
        if let Some(info)=self.class_cache.get(&class) {return Ok(Rc::clone(info));}
        let name=self.object_name(class)?;
        let mut current=class; let mut seen=HashSet::new(); let mut player=false; let mut capsule=false; let mut vehicle=false; let mut loot=false;
        for _ in 0..24 {
            if current<4096 || !seen.insert(current) {break;}
            let parent_name=self.object_name(current)?;
            if parent_name=="STExtraPlayerCharacter" {player=true;}
            if parent_name=="CapsuleComponent" {capsule=true;}
            if parent_name=="STExtraVehicleBase" {vehicle=true;}
            if parent_name=="PickUpWrapperActor" {loot=true;}
            current=self.memory.raw_ptr(current+0x30)?;
        }
        let training=name=="BP_CharacterModelTaget_C";
        let kind=if name.starts_with("FollowPet_") {"Companion"} else if training {"TrainingTarget"} else if player {"Character"} else if vehicle {"Vehicle"} else if loot {"Loot"} else {"Other"}.to_string();
        let info=Rc::new(ClassInfo {name,player:player||training,vehicle,loot,capsule,kind});
        if self.class_cache.len()>=MAX_CACHE {self.class_cache.clear();}
        self.class_cache.insert(class,Rc::clone(&info)); Ok(info)
    }
    fn player_name(&mut self, object:u64) -> Result<String> {
        let descriptor=self.memory.read(object+0x960,16)?;
        let ptr=u64_at(&descriptor,0); let count=i32_at(&descriptor,8); let capacity=i32_at(&descriptor,12);
        if count<1 || count>65 || capacity<count || capacity>1024 {return Err("Player name FString unavailable".into());}
        let raw=self.memory.read(ptr,count as usize*2)?;
        let chars:Vec<u16>=raw.chunks_exact(2).map(|b|u16::from_le_bytes([b[0],b[1]])).take_while(|c|*c!=0).collect();
        let text=String::from_utf16_lossy(&chars);
        if text.is_empty() || text.chars().any(|c|c.is_control()) {return Err("Invalid player display name".into());}
        Ok(text)
    }
    fn snapshot(&mut self, inventory:bool, watch_loot:bool, delta:bool) -> Result<String> {
        let start=Instant::now(); let reads_before=self.memory.reads;
        let engine=self.memory.ptr(self.base+0xf621d90)?;
        let contexts=self.memory.ptr(engine+0xc40)?; let context_count=self.memory.int(engine+0xc48)?;
        if !(1..=32).contains(&context_count) {return Err("Invalid world context count".into());}
        let mut world=0;
        for i in 0..context_count {
            let context=self.memory.ptr(contexts+i as u64*8)?;
            if self.memory.read(context,1)?[0]==1 {world=self.memory.ptr(context+0x2a0)?;break;}
        }
        if world==0 {return Err("No active game world".into());}
        if world!=self.last_world {self.class_cache.clear();self.last_world=world;self.classification_at=None;self.static_refresh_at=None;self.classification_signature.clear();self.dynamic_actors.clear();}
        let persistent_level=self.memory.ptr(world+0x30)?;
        let level_header=self.memory.read(world+0x440,16)?;
        let level_array=u64_at(&level_header,0);let level_count=i32_at(&level_header,8);let level_capacity=i32_at(&level_header,12);
        if level_count<0 || level_count>128 || level_capacity<level_count || level_capacity>1024 {return Err("Invalid loaded-level array bounds".into());}
        let mut levels=vec![persistent_level];
        if level_count>0 {
            let loaded=self.memory.read(level_array,level_count as usize*8)?;
            for slot in loaded.chunks_exact(8) {let level=u64_at(slot,0);if level>=4096 && !levels.contains(&level) {levels.push(level);}}
        }
        let actor_provider_flag=self.memory.int(persistent_level+0x440)?;
        let actor_provider=self.memory.raw_ptr(persistent_level+0x438)?;
        let mut actor_count=0usize;let mut level_arrays=Vec::new();
        for level in &levels {
            let provider_flag=self.memory.int(*level+0x440)?;let provider=self.memory.raw_ptr(*level+0x438)?;
            if provider_flag!=0 && provider!=0 {return Err("Alternate loaded-level actor provider is active; the exact-build direct-array reader does not support this state".into());}
            let array_header=self.memory.read(*level+0xa0,16)?;
            let array=u64_at(&array_header,0);let count=i32_at(&array_header,8);let capacity=i32_at(&array_header,12);
            if count<0 || count as usize>MAX_ACTORS || capacity<count || capacity>65536 {return Err("Invalid loaded-level actor array bounds".into());}
            actor_count+=count as usize;
            if actor_count>MAX_ACTORS {return Err("Loaded-level actor total exceeds bounded reader limit".into());}
            level_arrays.push((*level,array,count as usize));
        }
        let delta_active=delta&&!inventory&&!watch_loot;
        let refresh_statics=delta_active&&self.static_refresh_at.is_none_or(|at|at.elapsed()>=Duration::from_millis(200));
        let rescan=!delta_active || refresh_statics || self.classification_at.is_none_or(|at|at.elapsed()>=Duration::from_millis(200)) || self.classification_signature!=level_arrays;
        let mut candidates=Vec::new();
        if rescan {
            for (_,array,count) in &level_arrays {
                if *count>0 {let pointers=self.memory.read(*array,*count*8)?;candidates.extend(pointers.chunks_exact(8).map(|slot|u64_at(slot,0)));}
            }
        } else {candidates.extend_from_slice(&self.dynamic_actors);}
        let net_driver=self.memory.ptr(world+0x38)?; let connection=self.memory.ptr(net_driver+0x78)?; let controller=self.memory.ptr(connection+0x30)?;
        let pawn=self.memory.ptr(controller+0x528)?; let camera=self.memory.ptr(controller+0x548)?;
        let root=self.memory.ptr(pawn+0x208)?;
        let origin=self.memory.vec(root+0x220)?; let local_team=self.memory.int(pawn+0x998)?;
        let camera_info=self.memory.read(camera+0x530,56)?;
        let camera_position=Vec3{x:f32_at(&camera_info,0),y:f32_at(&camera_info,4),z:f32_at(&camera_info,8)};
        let camera_rotation=Vec3{x:f32_at(&camera_info,24),y:f32_at(&camera_info,28),z:f32_at(&camera_info,32)};
        let locked=self.memory.float(camera+0x4cc)?;
        let fov=if locked.is_finite()&&locked>0.0 {locked} else {f32_at(&camera_info,36)};
        let aspect=f32_at(&camera_info,52);
        if !camera_position.valid() || !camera_rotation.valid() || !fov.is_finite() || !(1.0..179.0).contains(&fov) {return Err("Invalid camera values".into());}
        let mut actors=Vec::new(); let mut static_actors=Vec::new();let mut next_dynamic=Vec::new();let mut skipped=0;let mut seen_actors=HashSet::new();
        for actor in candidates {
            if actor<4096 || !seen_actors.insert(actor) {continue;}
            let row=(||->Result<Option<(String,bool)>> {
                let header=self.memory.read(actor,0x28)?;
                let class=u64_at(&header,0x10); let info=self.class_info(class)?;
                if delta_active&&rescan&&(info.player||info.vehicle)&&info.kind!="Companion" {next_dynamic.push(actor);}
                if delta_active&&info.loot&&!refresh_statics {return Ok(None);}
                if watch_loot && !info.loot {return Ok(None);}
                if !inventory && ((!info.player && !info.vehicle && !info.loot) || info.kind=="Companion") {return Ok(None);}
                // Pooled pickup wrappers remain in the level array while hidden at world zero.
                // The inspected AActor bHidden bit distinguishes these from active ground loot.
                if !inventory && info.loot && self.memory.read(actor+0xe8,1)?[0]&1!=0 {return Ok(None);}
                let object_name=self.name(i32_at(&header,0x18)).unwrap_or_else(|_|info.name.clone());
                let position=self.memory.ptr(actor+0x208).and_then(|root|self.memory.vec(root+0x220));
                if !inventory && position.is_err() {return Ok(None);}
                let position=position.unwrap_or_default();
                let mut fuel=0.0; let mut max_fuel=0.0; let mut has_fuel=false; let mut has_health=false;
                let mut is_bot=None; let mut item_id=None; let mut quantity=0; let mut group=""; let mut debug_state=String::new();
                let (health,max_health,team,name)=if info.player {
                    let health=self.memory.float(actor+0xe60)?; let max_health=self.memory.float(actor+0xe64)?;
                    let team=self.memory.int(actor+0x998)?;
                    if !inventory && (!health.is_finite()||!max_health.is_finite()||health<=0.0||max_health<=0.0||health>max_health*1.1||max_health>100000.0) {return Ok(None);}
                    has_health=health.is_finite()&&max_health.is_finite()&&max_health>0.0;
                    if info.kind=="Character" {
                        if let Ok(flag)=self.memory.read(actor+0xa40,1) {
                            is_bot=if flag[0]!=0 {Some(true)} else if info.name=="BP_PlayerPawn_ST_AI_C" {None} else {Some(false)};
                        }
                    }
                    (health,max_health,team,self.player_name(actor).unwrap_or_else(|_|object_name.clone()))
                } else if info.vehicle {
                    let mut health=0.0; let mut max_health=0.0;
                    if let Ok(common)=self.memory.ptr(actor+0xc08) {
                        if let Ok(values)=self.memory.read(common+0x350,8) {
                            max_health=f32_at(&values,0); health=f32_at(&values,4);
                            has_health=health.is_finite()&&max_health.is_finite()&&health>=0.0&&max_health>0.0&&health<=max_health*1.1;
                        }
                        if let Ok(values)=self.memory.read(common+0x438,8) {
                            max_fuel=f32_at(&values,0);fuel=f32_at(&values,4);
                            has_fuel=fuel.is_finite()&&max_fuel.is_finite()&&fuel>=0.0&&max_fuel>0.0&&fuel<=max_fuel*1.1;
                        }
                    }
                    (health,max_health,-1,friendly_class(&info.name))
                } else if info.loot {
                    let fields=self.memory.read(actor+0x5f8,32)?;
                    let specific_id=i32_at(&fields,4);quantity=i32_at(&fields,24);
                    let can_pickup=fields[28]!=0;let picked_up=fields[29]!=0;
                    if watch_loot {
                        let hidden=self.memory.read(actor+0xe8,1)?[0];
                        debug_state=format!(",\"PickupState\":{{\"HiddenByte\":{hidden},\"CanPick\":{can_pickup},\"Picked\":{picked_up},\"Count\":{quantity}}}");
                    }
                    if !inventory && (!can_pickup||picked_up||specific_id<=0||quantity<=0||quantity>1_000_000) {return Ok(None);}
                    if specific_id>0 {item_id=Some(specific_id.to_string());}
                    group=loot_group(&info.name);
                    (0.0,0.0,-1,friendly_class(&info.name))
                } else {(0.0,0.0,-1,object_name.clone())};
                let height=(||->Result<f32> {
                    if !info.player {return Ok(0.0);}
                    let capsule=self.memory.ptr(actor+0x520)?;
                    let capsule_class=self.memory.ptr(capsule+0x10)?;
                    if !self.class_info(capsule_class)?.capsule {return Ok(180.0);}
                    let half=self.memory.float(capsule+0x9f8)?; let scale=self.memory.float(capsule+0x238)?;
                    let h=2.0*half*scale.abs();
                    if h.is_finite() && (20.0..=500.0).contains(&h) {Ok(h)} else {Ok(180.0)}
                })().unwrap_or(180.0);
                let bot_json=match is_bot {Some(true)=>"true",Some(false)=>"false",None=>"null"};
                let item_json=item_id.as_ref().map(|id|quoted(id)).unwrap_or_else(||"null".into());
                Ok(Some((format!("{{\"Address\":\"0x{actor:x}\",\"Name\":{},\"ObjectName\":{},\"Class\":{},\"Kind\":{},\"IsPlayer\":{},\"IsBot\":{bot_json},\"Position\":{},\"Health\":{},\"MaxHealth\":{},\"HasHealth\":{has_health},\"Fuel\":{},\"MaxFuel\":{},\"HasFuel\":{has_fuel},\"ItemId\":{item_json},\"Quantity\":{quantity},\"LootGroup\":{},\"Height\":{},\"Team\":{team}{debug_state}}}",quoted(&name),quoted(&object_name),quoted(&info.name),quoted(&info.kind),info.player,position.json(),number(health),number(max_health),number(fuel),number(max_fuel),quoted(group),number(height)),info.loot)))
            })();
            match row {Ok(Some((row,is_static)))=>if delta_active&&is_static {static_actors.push(row);} else {actors.push(row);},Ok(None)=>{},Err(_)=>skipped+=1}
        }
        if delta_active&&rescan {self.dynamic_actors=next_dynamic;self.classification_signature=level_arrays;self.classification_at=Some(start);}
        if refresh_statics {self.static_refresh_at=Some(start);}
        let static_fragment=if refresh_statics {format!(",\"StaticActors\":[{}]",static_actors.join(","))} else {String::new()};
        let elapsed=start.elapsed().as_secs_f64()*1000.0;
        Ok(format!("{{\"ReaderPid\":{},\"Session\":{},\"CameraPosition\":{},\"CameraRotation\":{},\"Fov\":{},\"AspectRatio\":{},\"Origin\":{},\"LocalPawn\":\"0x{pawn:x}\",\"LocalTeam\":{local_team},\"LevelCount\":{},\"ActorCount\":{actor_count},\"ActorProviderFlag\":{actor_provider_flag},\"ActorProvider\":\"0x{actor_provider:x}\",\"Skipped\":{skipped},\"ReadOperations\":{},\"ReadMilliseconds\":{elapsed:.3},\"Actors\":[{}]{static_fragment}}}",process::id(),quoted(&self.session),camera_position.json(),camera_rotation.json(),number(fov),number(aspect),origin.json(),levels.len(),self.memory.reads-reads_before,actors.join(",")))
    }
}

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
